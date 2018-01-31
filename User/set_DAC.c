#include "stm32f4xx_dac.h"

void set_DAC(GPIO_InitTypeDef GPIO_InitStructure, DAC_InitTypeDef DAC_InitStructure)
{
    /* DAC Setting ---------------------------------------------------------*/
  
    /* GPIOA clock enable (to be used with DAC) */
    RCC_AHB1PeriphClockCmd(RCC_AHB1Periph_GPIOA, ENABLE);
    /* DAC Periph clock enable */
    RCC_APB1PeriphClockCmd(RCC_APB1Periph_DAC, ENABLE);

#if 1
    /* DAC channel 1 (DAC_OUT1 = PA.4) configuration */
    GPIO_InitStructure.GPIO_Pin = GPIO_Pin_4;
    GPIO_InitStructure.GPIO_Mode = GPIO_Mode_AN;
    GPIO_InitStructure.GPIO_PuPd = GPIO_PuPd_NOPULL;
    GPIO_Init(GPIOA, &GPIO_InitStructure);

    DAC_InitStructure.DAC_Trigger = DAC_Trigger_None;
    DAC_InitStructure.DAC_WaveGeneration = DAC_WaveGeneration_None;
    DAC_InitStructure.DAC_OutputBuffer = DAC_OutputBuffer_Enable;
    DAC_InitStructure.DAC_LFSRUnmask_TriangleAmplitude = DAC_TriangleAmplitude_4095;
    DAC_Init(DAC_Channel_1, &DAC_InitStructure);

    DAC_SetChannel1Data(DAC_Align_12b_L, 0x7FF0);
    //DAC_SetChannel1Data(DAC_Align_12b_R, 0);        // 초기값 = 0
    DAC_Cmd(DAC_Channel_1, ENABLE);
#endif
    
#if 0
    /* DAC channel 2 (DAC_OUT1 = PA.5) configuration */
    GPIO_InitStructure.GPIO_Pin = GPIO_Pin_5;
    GPIO_InitStructure.GPIO_Mode = GPIO_Mode_AN;
    GPIO_InitStructure.GPIO_PuPd = GPIO_PuPd_NOPULL;
    GPIO_Init(GPIOA, &GPIO_InitStructure);

    DAC_InitStructure.DAC_Trigger = DAC_Trigger_None;
    DAC_InitStructure.DAC_WaveGeneration = DAC_WaveGeneration_None;
    DAC_InitStructure.DAC_OutputBuffer = DAC_OutputBuffer_Enable;
    DAC_InitStructure.DAC_LFSRUnmask_TriangleAmplitude = DAC_TriangleAmplitude_4095;
    DAC_Init(DAC_Channel_2, &DAC_InitStructure);

    DAC_SetChannel1Data(DAC_Align_12b_R, 0);        // 초기값 = 0
    DAC_Cmd(DAC_Channel_2, ENABLE);
#endif    
}

