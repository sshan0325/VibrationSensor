/**
  ******************************************************************************
  * @file    SysTick/stm32f4xx_it.c 
  * @author  MCD Application Team
  * @version V1.0.0
  * @date    19-September-2011
  * @brief   Main Interrupt Service Routines.
  *          This file provides template for all exceptions handler and 
  *          peripherals interrupt service routine.
  ******************************************************************************
  * @attention
  *
  * THE PRESENT FIRMWARE WHICH IS FOR GUIDANCE ONLY AIMS AT PROVIDING CUSTOMERS
  * WITH CODING INFORMATION REGARDING THEIR PRODUCTS IN ORDER FOR THEM TO SAVE
  * TIME. AS A RESULT, STMICROELECTRONICS SHALL NOT BE HELD LIABLE FOR ANY
  * DIRECT, INDIRECT OR CONSEQUENTIAL DAMAGES WITH RESPECT TO ANY CLAIMS ARISING
  * FROM THE CONTENT OF SUCH FIRMWARE AND/OR THE USE MADE BY CUSTOMERS OF THE
  * CODING INFORMATION CONTAINED HEREIN IN CONNECTION WITH THEIR PRODUCTS.
  *
  * <h2><center>&copy; COPYRIGHT 2011 STMicroelectronics</center></h2>
  ******************************************************************************
  */ 

/* Includes ------------------------------------------------------------------*/
#include "stm32f4xx_it.h"
#include "main.h"

/** @addtogroup STM32F4_Discovery_Peripheral_Examples
  * @{
  */

/** @addtogroup SysTick_Example
  * @{
  */ 

/* Private variables ---------------------------------------------------------*/
// ���� �߰� 17.06.12
unsigned char Rx_Compli_Flag = RESET;
//unsigned char Packet_Number = 0;
unsigned char Rx_Count = 0 ;
unsigned char Rx_Buffer[10];
unsigned char Rx_Buffer_temp[10];

unsigned char My_ID = 0x01;

extern char Output[TEST_LENGTH_SAMPLES/2][10];
extern uint8_t flag_ID;
extern uint8_t flag_CMD;
extern uint8_t SensingFactor;
uint8_t k, j;

/******************************************************************************/
/*            Cortex-M3 Processor Exceptions Handlers                         */
/******************************************************************************/

/**
  * @brief   This function handles NMI exception.
  * @param  None
  * @retval None
  */
void NMI_Handler(void)
{
}

/**
  * @brief  This function handles Hard Fault exception.
  * @param  None
  * @retval None
  */
void HardFault_Handler(void)
{
  /* Go to infinite loop when Hard Fault exception occurs */
  while (1)
  {
  }
}

/**
  * @brief  This function handles Memory Manage exception.
  * @param  None
  * @retval None
  */
void MemManage_Handler(void)
{
  /* Go to infinite loop when Memory Manage exception occurs */
  while (1)
  {
  }
}

/**
  * @brief  This function handles Bus Fault exception.
  * @param  None
  * @retval None
  */
void BusFault_Handler(void)
{
  /* Go to infinite loop when Bus Fault exception occurs */
  while (1)
  {
  }
}

/**
  * @brief  This function handles Usage Fault exception.
  * @param  None
  * @retval None
  */
void UsageFault_Handler(void)
{
  /* Go to infinite loop when Usage Fault exception occurs */
  while (1)
  {
  }
}

/**
  * @brief  This function handles SVCall exception.
  * @param  None
  * @retval None
  */
void SVC_Handler(void)
{
}

/**
  * @brief  This function handles Debug Monitor exception.
  * @param  None
  * @retval None
  */
void DebugMon_Handler(void)
{
}

/**
  * @brief  This function handles PendSVC exception.
  * @param  None
  * @retval None
  */
void PendSV_Handler(void)
{
}

/**
  * @brief  This function handles SysTick Handler.
  * @param  None
  * @retval None
  */
void SysTick_Handler(void)
{
    TimingDelay_Decrement();                                      // Delay�� ���� �Լ�
}

/******************************************************************************/
/*                 STM32F4xx Peripherals Interrupt Handlers                   */
/*  Add here the Interrupt Handler for the used peripheral(s) (PPP), for the  */
/*  available peripheral interrupt handler's name please refer to the startup */
/*  file (startup_stm32f4xx.s).                                               */
/******************************************************************************/

void USART2_IRQHandler(void)                                   // RCU ��Ŷ ���� ���ͷ�Ʈ
{	  
    if(USART_GetITStatus(USART2, USART_IT_RXNE) != RESET)
    {	
        Rx_Buffer[Rx_Count] = USART_ReceiveData(USART2);
        if (Rx_Buffer[0] == 0x02)
        {
          Rx_Count++;        
        }
        else
        {
          Rx_Count=0;
        }
             
        if(Rx_Count == 6)
        {	                  
           j = 0;
           
           /* ��Ŷ �Ǻ� */
           while(1)
           {
                if(Rx_Buffer[j] == 0x02)                    // STX
                {
                    if(Rx_Buffer[j+1] == My_ID)          // ID (0A,  0B,  0C,  0D) -> ID (01 ~ 0F)
                    {
                        flag_ID = 1;
                        
                        if(Rx_Buffer[j+3] == 0xC1)        // CMD (01 - default,  C1 - ������ ������ �۽� ���)
                        {
                            flag_CMD = 1;
                        }
                        else if(Rx_Buffer[j+3] == 0xC2)         // CMD (C2 - ������ factor�� ���� ��û)
                        {
                            flag_CMD = 2;
                        }
                        else if(Rx_Buffer[j+3] == 0xC5)         // CMD (C2 - ������ factor�� ���� ��û)
                        {
                            SensingFactor=Rx_Buffer[j+4];
                        }
                        else
                        {
                            flag_CMD = 0;
                        }
                        /*
                           [17.09.20]
                           ���Ŀ� ������ factor�� ��û�ϴ� CMD �߰��� ����
                           --> ���� �������� factor(������ ��� �� �����) ������
                           --> �������� ���忡�� ���� ���ݾ� �ٲ� �� �����Ƿ� ���������� Ȯ���� �� �ְ� �ϱ� ����.
                        */
                        
                        break;
                    }
                    else
                    {
                        break;
                    }
                }
                else
                {
                  j++;
                  if (j>4)                    
                  {
                    j=0;
                    break;
                  }
                }
            }
         
           /*
            if(Rx_Buffer[2] == 0x0A)          // ID (0A,  0B,  0C,  0D)
            {
                flag_ID = 1;
            }
           */
            Rx_Compli_Flag = SET ; 
            //Packet_Number = Rx_Count-1;                         // ��Ŷ ���� ����
            Rx_Count = 0;      
        }
    }
}


/**
  * @}
  */ 

/**
  * @}
  */ 

/******************* (C) COPYRIGHT 2011 STMicroelectronics *****END OF FILE****/
