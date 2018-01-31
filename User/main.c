/**
  ******************************************************************************
  * @file     main.c 
  * @date   2017.09.20
  ******************************************************************************
  * @attention
  * RS485���
  * ��� ������: FFT ũ��, ���ļ�, ũ�� ��հ�(����2~5), �ܰ� (STX���� �ؼ� �� 13����Ʈ) --> 5�� ����
  ******************************************************************************  
  * @ ������
  ****************************************************************************** 
  */ 

/* Includes ---------------------------------------------------------------*/
#include "main.h"
#include "stdio.h"
#include "arm_math.h" 

/* Private define -----------------------------------------------------------*/
// main.h�� �Űܰ�
//#define TEST_LENGTH_SAMPLES 512                 // ���� ���� ���� : 2048, 512, 128, 32 (arm_cfft_radix4_f32 �ּ�)

/* Private variables ---------------------------------------------------------*/
GPIO_InitTypeDef GPIO_InitStructure;
static __IO uint32_t TimingDelay;
int8_t Buffer[6];
int8_t temp_Z = 0;
uint8_t flag_SENSE = 0;
uint8_t flag_ID = 0;
uint8_t flag_CMD;
char str[100];


// FFT ��
uint32_t fftSize = TEST_LENGTH_SAMPLES/2;       // ���� ���� ���� : 1024, 256, 64, 16 (arm_cfft_radix4_f32 �ּ�)
uint32_t ifftFlag = 0; 
uint32_t doBitReverse = 1;
uint32_t testIndex = 0; 

float32_t freq = 0;
float32_t testInput_[TEST_LENGTH_SAMPLES];
static float32_t testOutput_[TEST_LENGTH_SAMPLES/2]; 
static float32_t testOutput_tmp[TEST_LENGTH_SAMPLES/2];

/*  �۽� ����  (Output[][])
 *  ���� ������ ��û�� : STX | ID | LEN | CMD1 | SEQ | D1 | D2 | D3 | D4 | D5 | D6 | D7 | CS 
 *  ������ factor ��û�� : STX | ID | LEN | CMD2 | SEQ | D1 | D2 | D3 | D4 | D5 | D6 | Dummy | CS 
 *  �� D1~D7�� ���� �ڼ��� ������ �ش� ȣ�� �ҽ� �κ��� �ּ��� ������ ��
 */
char Output[TEST_LENGTH_SAMPLES/2][14];

// RS485 �ۼ��� ��
extern unsigned char Rx_Compli_Flag;
extern unsigned char Packet_Number;
extern unsigned char Rx_Count;
extern unsigned char Rx_Buffer[10];
extern unsigned char Rx_Buffer_temp[10];
extern unsigned char My_ID;

uint8_t seq = 0;

unsigned char Rx_Buffer_copy[10];

/* Private function prototypes -----------------------------------------------*/
void Delay(__IO uint32_t nTime);
void ConfigureUsart(int baudrate);
void SendData(USART_TypeDef* USARTx, volatile char *s);
void Packet_handler(void);
void USART2_IRQHandler(void);
unsigned char Check_Byte(char* packet);
double Rounding(double x, int digit);

// ���� ũ�� ��հ�, dB ����
int calc_state(float32_t value);

float dB = 0;
float factor_0 = 0.1738;          // ������: y=0.009*x+0.1738  ->  x=(y-0.1738)*(1/0.009) = (y-0.1738)*111.111
uint8_t tmp_0[3];
float factor_1 = 111.111;
uint8_t tmp_1[4];
uint8_t SensingFactor=4;

int rs485_dir(int rx)
{
  
   if(rx==0) GPIO_ResetBits(GPIOA, GPIO_Pin_1);
   else GPIO_SetBits(GPIOA, GPIO_Pin_1);
  
   return rx;
}


int main(void)
{
    uint16_t i, j, k, t;
    float32_t max[5] = { 0, 0, 0, 0, 0 };      // ���� ũ��
    int index[5] = { 0, 0, 0, 0, 0 };           // ���ļ� ũ��
    float32_t max_avr = 0;                       // ���� ũ�� ���� 2~5���� ��հ� �����
    int max_state = 0;
    int dBValue = 0;
    
    
    /* LED Setting */
    GPIO_InitTypeDef GPIO_InitStruct;
    RCC_AHB1PeriphClockCmd(RCC_AHB1Periph_GPIOD, ENABLE);
    
    GPIO_InitStruct.GPIO_Pin = GPIO_Pin_12|GPIO_Pin_13|GPIO_Pin_14|GPIO_Pin_15;
    GPIO_InitStruct.GPIO_Mode = GPIO_Mode_OUT;
    GPIO_InitStruct.GPIO_Speed = GPIO_Speed_100MHz;
    GPIO_InitStruct.GPIO_OType = GPIO_OType_PP;
    GPIO_InitStruct.GPIO_PuPd = GPIO_PuPd_NOPULL;
    GPIO_Init(GPIOD, &GPIO_InitStruct);
     
    /* RTC Pin Setting */
    RCC_AHB1PeriphClockCmd(RCC_AHB1Periph_GPIOA, ENABLE);
    GPIO_InitStruct.GPIO_Pin = GPIO_Pin_1;
    GPIO_InitStruct.GPIO_Mode = GPIO_Mode_OUT;
    GPIO_InitStruct.GPIO_Speed = GPIO_Speed_100MHz;
    GPIO_InitStruct.GPIO_OType = GPIO_OType_PP;
    GPIO_InitStruct.GPIO_PuPd = GPIO_PuPd_NOPULL;
    GPIO_Init(GPIOA, &GPIO_InitStruct);

    /* Setup SysTick Timer for 1 msec interrupts*/
    if (SysTick_Config(SystemCoreClock /1000))
    { 
        while (1);
    }

    /* UART Setting -------------------------------------------------------*/
    ConfigureUsart(9600);   // setup usart 1 with a baudrate of 9600
      
    /* LIS302DL Setting ----------------------------------------------------*/
    LIS302DL_InitTypeDef  LIS302DL_Init;
    LIS302DL_InterruptConfigTypeDef LIS302DL_Interrupt;  
    set_LIS302DL(LIS302DL_Init, LIS302DL_Interrupt);
          
    Delay(30);    // Turn-on time = 3/Output data Rate = 3/100 = 30ms

    /* DAC Setting -------------------------------------------------------*/
    GPIO_InitTypeDef GPIO_Init;
    DAC_InitTypeDef DAC_Init;
    set_DAC(GPIO_Init, DAC_Init);
    
    /* FFT Setting --------------------------------------------------------*/
    arm_status status; 
    arm_cfft_radix4_instance_f32 S; 
    float32_t maxValue; 
     
    status = ARM_MATH_SUCCESS; 

  
     /* RTC status = Low */
     rs485_dir(0); 
        
    while (1)
    {      
        LIS302DL_Read(Buffer, LIS302DL_OUT_Z_ADDR, 6); 
       
        /* ���� ũ�� ��ȭ�� ������ ��� */
        if(abs(temp_Z - Buffer[0]) >= SensingFactor)
        {
            flag_SENSE = 1;                                             // ���� ���� �÷��� - On  
            GPIO_SetBits(GPIOD, GPIO_Pin_12);                 // LED(Green) - On
        }
        else
        {
            /* RTC status = Low */
            rs485_dir(0); 
        }
              
#if 0
        /* RX������ Data Buffer TX�ϱ� */
        if(Rx_Compli_Flag == SET)
        {          
            Rx_Compli_Flag = RESET;        
            
            if(flag_ID == 1)                                                // �ش� ID ���� Flag
            {
                  flag_ID = 0;
                  GPIO_SetBits(GPIOD, GPIO_Pin_15);           // LED(Blue) - On
        
                  rs485_dir(1);                                             // RTC High - TX
                  Delay(40);                                                // **�ݵ�� �ʿ�** - ������ ��Ŷ 6-7���� �ݺ��� �� TX��
                  
                  for(k=0; k<5; k++)                                     // ���� 5���� ���� ������(13 ����Ʈ) ���
                  {
                      for(j=0; j<13; j++)                                  
                      {
                          while( !(USART2->SR & 0x00000040) );
                          USART_SendData(USART2, Output[k][j]);
                          while (USART_GetFlagStatus(USART2, USART_FLAG_TC) == RESET);
                      }
                      Delay(40); 
                  }

                  rs485_dir(0);                                             // RTC Low - RX
                  GPIO_ResetBits(GPIOD, GPIO_Pin_15);        // LED(Blue) - Off
            }
        }
#endif
        
         /* ������ factor�� ���� ��� */
        if(flag_CMD == 2)
        {
            /* �ڵ� ���� ���� ��ȯ */
            flag_CMD = 1;
            
            // factor0 �κ�
            tmp_0[0] = (int)(factor_0);                                         // ����
            tmp_0[1] = (int)((factor_0-tmp_0[0])*10000)/fftSize;     // �Ҽ�1
            tmp_0[2] = (int)((factor_0-tmp_0[0])*10000)%fftSize;    // �Ҽ�2
            // factor1 �κ�
            tmp_1[0] = (int)(factor_1);                                        // ����
            tmp_1[1] = (int)((factor_1-tmp_1[0])*10000)/fftSize;    // �Ҽ�1
            tmp_1[2] = (int)((factor_1-tmp_1[0])*10000)%fftSize;   // �Ҽ�2 
            
            /* ������ factor�� ������Ʈ */
            Output[0][0] = 0x02;                                       //  STX
            Output[0][1] = My_ID;                                    //  ID
            Output[0][2] = 0x0E;                                      //  Data length
            Output[0][3] = 0xB1;                                      //  Command
            Output[0][4] = seq;                                       //  Sequence                    
            Output[0][5] = tmp_0[2];                                //  D1 --> factor0 �Ҽ�2
            Output[0][6] = tmp_0[1];                                //  D2 --> factor0 �Ҽ�1
            Output[0][7] = tmp_0[0];                                //  D3 --> factor0 ����
            Output[0][8] = tmp_1[2];                                //  D4 --> factor1 �Ҽ�2
            Output[0][9] = tmp_1[1];                                //  D5 --> factor1 �Ҽ�1
            Output[0][10] = tmp_1[0];                               //  D6 --> factor1 ����
            Output[0][11] = 0;                                          //  ����
            Output[0][12] = 0;                                          //  ����
            Output[0][13] = Check_Byte(Output[0]);            //  CS
        
            seq++;

            if(seq > 255)
            {
                seq = 0;
            }
            
            /* ��Ŷ ���� */                    
            GPIO_SetBits(GPIOD, GPIO_Pin_15);              // LED(Blue) On
            rs485_dir(1);                                                // RTC High - TX
            Delay(40);    
            
            for(j=0; j<13; j++)
            {
                while( !(USART2->SR & 0x00000040) );
                USART_SendData(USART2, Output[0][j]);
                while (USART_GetFlagStatus(USART2, USART_FLAG_TC) == RESET);
            }
            
            rs485_dir(0);                                                // RTC Low - RX
            GPIO_ResetBits(GPIOD, GPIO_Pin_15);           // LED(Blue) Off
        }

        while(flag_SENSE == 1)                                       // ������ ���� Flag 
        {       
            LIS302DL_Read(Buffer, LIS302DL_OUT_Z_ADDR, 6); 
            Delay(1);                                                       // ���� = 1000/Fs (ms)
                    
            /* DAC ��� - mg������ ��� */
            DAC_SetChannel1Data(DAC_Align_12b_R, Buffer[0]*50);  // 980mg <-> 38h (decimal ��: 56)   -> mg������ ���� ���Ͽ� 17.5 ���ϱ� (56*17.5 = 980)
                                                                                             // �������� �ΰ� ���� �� DAC�� : 980/4095*2.5(V) = 0.6(V)
          
            testInput_[i*2] = (Buffer[0])*17.5/980;
            testInput_[i*2+1] = 0;
            i++;
          
            /* FFT ���� */
            if(i == fftSize)
            {
                /* �ʱ�ȭ */
                i = 0;
                flag_SENSE = 0;
                
                /* Initialize the CFFT/CIFFT module */  
                status = arm_cfft_radix4_init_f32(&S, fftSize, ifftFlag, doBitReverse); 
        
                /* Process the data through the CFFT/CIFFT module */ 
                arm_cfft_radix4_f32(&S, testInput_);
                
                /* Process the data through the Complex Magnitude Module for calculating the magnitude at each bin */ 
                arm_cmplx_mag_f32(testInput_, testOutput_, fftSize);  

                testOutput_[0] = 0;                                      // 0��° �� --> DC(0Hz)
                
                /* Calculates maxValue and returns corresponding BIN value */ 
                arm_max_f32(testOutput_, fftSize, &maxValue, &testIndex);  

                for (j = 0; j < fftSize; j++)
                {
                    testOutput_tmp[j] = testOutput_[j];
                }
                
                
                /* ũ�Ⱑ ū ���� 5���� ���� ������ �̱� */
                for (j = 0; j < fftSize/2; j++)
                {
                    if (testOutput_tmp[j] > max[0]) 
                    {
                        max[0] = testOutput_tmp[j];
                        index[0] = j;
                    }
                }

                testOutput_tmp[index[0]] = 0;                      // max���� �ִ� �ڸ��� 0���� �ʱ�ȭ
                for (j = 0; j < fftSize/2; j++)
                {
                    if (testOutput_tmp[j] > max[1]) 
                    {
                        max[1] = testOutput_tmp[j];
                        index[1] = j;
                    }
                }

                testOutput_tmp[index[1]] = 0;                      // max���� �ִ� �ڸ��� 0���� �ʱ�ȭ
                for (j = 0; j < fftSize/2; j++)
                {
                    if (testOutput_tmp[j] > max[2])
                    {
                        max[2] = testOutput_tmp[j];
                        index[2] = j;
                    }
                }
                
                testOutput_tmp[index[2]] = 0;                      // max���� �ִ� �ڸ��� 0���� �ʱ�ȭ
                for (j = 0; j < fftSize/2; j++)
                {
                    if (testOutput_tmp[j] > max[3])
                    {
                        max[3] = testOutput_tmp[j];
                        index[3] = j;
                    }
                }
                
                testOutput_tmp[index[3]] = 0;                      // max���� �ִ� �ڸ��� 0���� �ʱ�ȭ
                for (j = 0; j < fftSize/2; j++)
                {
                    if (testOutput_tmp[j] > max[4])
                    {
                        max[4] = testOutput_tmp[j];
                        index[4] = j;
                    }
                }
                
                /* ���� 2~5���� FFTũ���� ��հ� */
                for(t=1; t<5; t++)
                {
                    max_avr += max[t];
                }                
                max_avr = max_avr/4;
                
                /* ��հ��� ���� �ܰ� ��� */
                max_state = calc_state(max_avr);
                dBValue = (max_avr - factor_0)*factor_1;
                
                /* FFT ������ ���� */ 
                for(t=0; t<5; t++)
                {                  
                    Output[t][0] = 0x02;                                       //  STX
                    Output[t][1] = My_ID;                                    //  ID
                    Output[t][2] = 0x0D;                                      //  Data length
                    Output[t][3] = 0xA1;                                      //  Command
                    Output[t][4] = seq;                                       //  Sequence                    
                    Output[t][5] = (int)(index[t]*400)%fftSize;       //  D1 --> frequency = (n*Fs)/FFT_size [Hz]
                    Output[t][6] = (int)(index[t]*400)/fftSize;        //  D2 --> frequency = (n*Fs)/FFT_size [Hz]
                    Output[t][7] = (int)(max[t]*100)%fftSize;        //   D3 --> ���� ũ�� �Ҽ� �κ�
                    Output[t][8] = (int)(max[t]*100)/fftSize;         //  D4 --> ���� ũ�� ���� �κ�
                    Output[t][9] = (int)(max_avr*100)%fftSize;      //  D5 --> ��հ� �Ҽ� �κ�
                    Output[t][10] = (int)(max_avr*100)/fftSize;      //  D6 --> ��հ� ���� �κ�
                    Output[t][11] = max_state;                            //  D7 --> ��հ� �ܰ�
                    Output[t][12] = dBValue;
                    Output[t][13] = Check_Byte(Output[t]);          //  CS
                
                    seq++;

                    if(seq > 255)
                    {
                        seq = 0;
                    }
                    
                    /* Index, Max ���� �ʱ�ȭ */
                    index[t] = 0;
                    max[t] = 0;
                }
                
                // ��հ� �ʱ�ȭ
                max_avr = 0;
                
                
                /* flag_CMD�� 1�� �Ǳ� �������� �ڵ� ���۸�� ��Ȱ��ȭ */
                if(flag_CMD == 1)
                {                  
                      GPIO_SetBits(GPIOD, GPIO_Pin_15);              // LED(Blue) On
                      rs485_dir(1);                                                // RTC High - TX
                      Delay(40);    
                      
                      for(k=0; k<5; k++)                                        // ���� 5���� ���� �����͸� ���
                      {
                          for(j=0; j<14; j++)
                          {
                              while( !(USART2->SR & 0x00000040) );
                              USART_SendData(USART2, Output[k][j]);
                              while (USART_GetFlagStatus(USART2, USART_FLAG_TC) == RESET);
                          }

                          Delay(40);                                               // 40ms �������� TX     
                      }
                      
                      rs485_dir(0);                                                // RTC Low - RX
                      GPIO_ResetBits(GPIOD, GPIO_Pin_15);           // LED(Blue) Off
                } 
                GPIO_ResetBits(GPIOD, GPIO_Pin_12);                // LED(Green) - Off
            }
                     
            /* ������ ���� Z�� �� ���� */
            temp_Z = Buffer[0];
        }

    }

}


/* Rounding ------------------------------------------------------------*/
double Rounding( double x, int digit )
{
    return ( floor( (x) * pow( 10, digit ) + 0.5f ) / pow( 10, digit ) );
}


/* CheckSum------------------------------------------------------------*/
unsigned char Check_Byte(char* packet)
{
    uint32_t i; 
    uint32_t cbyte = 0x02;
    for (i = 1; i < (packet[2] - 1); i++)
    {
        cbyte ^= packet[i]; // XOR 
        cbyte++; // 1����
    }
    
    return cbyte;
}

/* ConfigureUsart ---------------------------------------------------------*/
void ConfigureUsart(int baudrate)
{
    //Configures the USART using pin B6 as TX and B7 as RX and the passed in baudrate
  
    //structures used configure the hardware
    GPIO_InitTypeDef GPIO_InitStruct_TX;
    GPIO_InitTypeDef GPIO_InitStruct_RX;
    USART_InitTypeDef USART_InitStruct;  
    NVIC_InitTypeDef NVIC_InitStructure;
    
    NVIC_InitStructure.NVIC_IRQChannel = USART2_IRQn;
    NVIC_InitStructure.NVIC_IRQChannelPreemptionPriority = 0;
    NVIC_InitStructure.NVIC_IRQChannelSubPriority = 0;
    NVIC_InitStructure.NVIC_IRQChannelCmd = ENABLE;
    NVIC_Init(&NVIC_InitStructure);
        
    //enable the clocks for the GPIOB and the USART
    RCC_APB1PeriphClockCmd(RCC_APB1Periph_USART2, ENABLE);
    RCC_AHB1PeriphClockCmd(RCC_AHB1Periph_GPIOA, ENABLE);
    

    //Initialise pins GPIOA 2 and GPIOA 3   
    GPIO_InitStruct_TX.GPIO_Pin = GPIO_Pin_2;
    GPIO_InitStruct_TX.GPIO_Mode = GPIO_Mode_AF; //we are setting the pin to be alternative function
    GPIO_InitStruct_TX.GPIO_Speed = GPIO_Speed_50MHz;
    GPIO_InitStruct_TX.GPIO_OType = GPIO_OType_PP;
    GPIO_InitStruct_TX.GPIO_PuPd = GPIO_PuPd_UP;
    GPIO_Init(GPIOA, &GPIO_InitStruct_TX);
    
    //Connect the TX pins to their alternate function pins
    GPIO_PinAFConfig(GPIOA, GPIO_PinSource2, GPIO_AF_USART2);     // USART2_TX
    
    //===============================================================================
    
    GPIO_InitStruct_RX.GPIO_Pin = GPIO_Pin_3;
    GPIO_InitStruct_RX.GPIO_Mode = GPIO_Mode_AF; //we are setting the pin to be alternative function
    GPIO_InitStruct_RX.GPIO_Speed = GPIO_Speed_50MHz;    
    GPIO_Init(GPIOA, &GPIO_InitStruct_RX);
    
    //Connect the  RX pins to their alternate function pins
    GPIO_PinAFConfig(GPIOA, GPIO_PinSource3, GPIO_AF_USART2);     // USART2_RX

 //=================================================================================
    
    //configure USART
    USART_InitStruct.USART_BaudRate = baudrate;
    USART_InitStruct.USART_WordLength = USART_WordLength_8b;
    USART_InitStruct.USART_StopBits = USART_StopBits_1;
    USART_InitStruct.USART_Parity = USART_Parity_No;
    USART_InitStruct.USART_HardwareFlowControl = USART_HardwareFlowControl_None;
    USART_InitStruct.USART_Mode = USART_Mode_Tx | USART_Mode_Rx; //enable send and receive (Tx and Rx)
    USART_Init(USART2, &USART_InitStruct);
    
    //Enable the interupt
    USART_ITConfig(USART2, USART_IT_RXNE, ENABLE);
    
    USART_Init(USART2, &USART_InitStruct);
    
    //finally this enables the complete USART2 peripheral
    USART_Cmd(USART2, ENABLE);
}

/* SendData ---------------------------------------------------------------*/
void SendData(USART_TypeDef* USARTx, volatile char *s)
{
    while(*s){
            // wait until data register is empty
            while( !(USARTx->SR & 0x00000040) );
            USART_SendData(USARTx, *s);
            *s++;
    }
}

/* calc_state---------------------------------------------------------------*/
int calc_state(float32_t value)
{
    int state = 0;
    
    //dB = (value-0.1738)*111.111;
    dB = (value - factor_0)*factor_1;
    
    if((dB>= 35)&&(dB<36))
    {
        state = 1;
    }
    else if((dB>= 36)&&(dB<37))
    {
        state = 2;
    }
    else if((dB>= 37)&&(dB<38))
    {
        state = 3;
    }
    else if((dB>= 38)&&(dB<39))
    {
        state = 4;
    }
    else if((dB>= 39)&&(dB<40))
    {
        state = 5;
    }
    else  if((dB>= 40)&&(dB<41))
    {
        state = 6;
    }
    else if((dB>= 41)&&(dB<42))
    {
        state = 7;
    }
    else if((dB>= 42)&&(dB<43))
    {
        state = 8;
    }
    else if((dB>= 43)&&(dB<44))
    {
        state = 9;
    }
    else if((dB>= 44)&&(dB<45))
    {
        state = 10;
    }
    else if((dB>= 45)&&(dB<46))
    {
        state = 11;
    }
    else if((dB>= 46)&&(dB<47))
    {
        state = 12;
    }
    else if((dB>= 47)&&(dB<48))
    {
        state = 13;
    }
    else if((dB>= 48)&&(dB<49))
    {
        state = 14;
    }
    else if((dB>= 49)&&(dB<50))
    {
        state = 15;
    }
    else  if((dB>= 50)&&(dB<51))
    {
        state = 16;
    }
    else if((dB>= 51)&&(dB<52))
    {
        state = 17;
    }
    else if((dB>= 52)&&(dB<53))
    {
        state = 18;
    }
    else if((dB>= 53)&&(dB<54))
    {
        state = 19;
    }
    else if((dB>= 54)&&(dB<55))
    {
        state = 20;
    }
    else if(dB >55)
    {
        state = 100;
    }
  
    return state;
}


/* Delay ------------------------------------------------------------------*/
void Delay(__IO uint32_t nTime)
{ 
  TimingDelay = nTime;

  while(TimingDelay != 0);
}

/* TimingDelay_Decrement --------------------------------------------------*/
void TimingDelay_Decrement(void)
{
  if (TimingDelay != 0x00)
  { 
    TimingDelay--;
  }
}

/* LIS302DL_TIMEOUT_UserCallback ------------------------------------------*/
uint32_t LIS302DL_TIMEOUT_UserCallback(void)
{
  /* MEMS Accelerometer Timeout error occured */
  while (1)
  {   
        // Blank
  }
}

#ifdef  USE_FULL_ASSERT
void assert_failed(uint8_t* file, uint32_t line)
{ 
  /* User can add his own implementation to report the file name and line number,
     ex: printf("Wrong parameters value: file %s on line %d\r\n", file, line) */

  /* Infinite loop */
  while (1)
  {
  }
}
#endif

/**
  * @}
  */ 

/**
  * @}
  */ 

/******************* (C) COPYRIGHT 2011 STMicroelectronics *****END OF FILE****/