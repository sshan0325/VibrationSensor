#include "stm32f4_discovery_lis302dl.h"

void set_LIS302DL(LIS302DL_InitTypeDef LIS302DL_InitStruct, LIS302DL_InterruptConfigTypeDef LIS302DL_InterruptStruct)
{
    /* LIS302DL Setting ---------------------------------------------------------*/
    // 이곳에 있던 Systick이 계속 10ms로 돌아가게 했었음...

    /* Set configuration of LIS302DL*/
    LIS302DL_InitStruct.Power_Mode = LIS302DL_LOWPOWERMODE_ACTIVE;
    LIS302DL_InitStruct.Output_DataRate = LIS302DL_DATARATE_400;                //LIS302DL_DATARATE_100;
    LIS302DL_InitStruct.Axes_Enable = LIS302DL_X_ENABLE | LIS302DL_Y_ENABLE | LIS302DL_Z_ENABLE;
    LIS302DL_InitStruct.Full_Scale = LIS302DL_FULLSCALE_2_3;
    LIS302DL_InitStruct.Self_Test = LIS302DL_SELFTEST_NORMAL;
    LIS302DL_Init(&LIS302DL_InitStruct);
          
    /* Set configuration of Internal High Pass Filter of LIS302DL*/
    LIS302DL_InterruptStruct.Latch_Request = LIS302DL_INTERRUPTREQUEST_LATCHED;
    LIS302DL_InterruptStruct.SingleClick_Axes = LIS302DL_CLICKINTERRUPT_Z_ENABLE;
    LIS302DL_InterruptStruct.DoubleClick_Axes = LIS302DL_DOUBLECLICKINTERRUPT_Z_ENABLE;
    LIS302DL_InterruptConfig(&LIS302DL_InterruptStruct);
    
    #if 0
    /* Set Internal HPF configuration of LIS302DL*/
    LIS302DL_HPFStruct.HighPassFilter_Data_Selection = LIS302DL_FILTEREDDATASELECTION_OUTPUTREGISTER;
    LIS302DL_HPFStruct.HighPassFilter_CutOff_Frequency = LIS302DL_HIGHPASSFILTERINTERRUPT_OFF;
    LIS302DL_HPFStruct.HighPassFilter_Interrupt = LIS302DL_HIGHPASSFILTER_LEVEL_3;
    LIS302DL_FilterConfig(&LIS302DL_HPFStruct);
    #endif 
}

