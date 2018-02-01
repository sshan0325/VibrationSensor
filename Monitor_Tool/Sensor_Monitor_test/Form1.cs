using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.IO.Ports;
using System.Net;

// '1A'를 char가 아니라 eof로 받아들이는 것 방지
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Reflection;

namespace LED_control_test
{
    public partial class Form1 : Form
    {
        //-------------- '1A'를 eof가 아닌 char로 받기 위한 코드 --------------
        [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern bool SetCommMask(
            SafeFileHandle hFile,
            int dwEvtMask
        );
        //---------------------------------------------------------------------

        SerialPort sPort;

        // 폴링용 플래그
        bool flag_stop = false;     // 폴링 정지 flag
        bool flag_start = false;    // 폴링 시작 flag
        bool flag_next_pol = false;     // 5패킷 다 받으면 or 다른 ID이면 넘어가기

        // ID 리스트 (combo box / hex버젼)
        //string[] ID_list = { "0A", "0B", "0C", "0D" };
        //byte[] ID_list_hex = { 0x0A, 0x0B, 0x0C, 0x0D };
        string[] ID_list = { "01", "02", "03", "04" };
        byte[] ID_list_hex = { 0x01, 0x02, 0x03, 0x04 };
        byte[] sen_hex = { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x10 };
        
        // 센서 요청 버퍼 (STX, ID, LEN, CMD, SEQ, CS) : 6개
        byte[] TxBuffer = { 0x02, 0x0A, 0x06, 0xC1, 0x00, 0xFF };

        // 데이터 박스 이동 플래그
        int next_box = 0;

        // 체크용 flag(ID리스트의 'All' / ID가 제대로 들어온 여부)
        byte[] ID_chk = new byte[1];

        // ID 저장용 -> 멀티 제어 폴링
        byte[] scan_check = { 0, 0, 0, 0, 0, 0, 0, 0 };
        int[] save_id = { 0, 0, 0, 0, 0, 0, 0, 0 };
        int ID_pol = 0;

        // calc_hex 함수용 변수
        string[] freq_Log = new string[5];
        string[] amp_Log = new string[5];
        string[] dB_Log = new string[5];
        string[] state_Log = new string[5];
        decimal[] freq_group = new decimal[5];
        decimal[] amp_group = new decimal[5];
        decimal[] dB_group = new decimal[5];

        // 주파수 5개 표시용
        string freq_val_1;
        string freq_val_2;
        string freq_val_3;
        string freq_val_4;
        string freq_val_5;

        // 크기 5개 표시용
        string amp_val_1;
        string amp_val_2;
        string amp_val_3;
        string amp_val_4;
        string amp_val_5;

        // 패킷 카운트용
        int cnt_val = 0;

        // Sequence용 변수
        Int16 seq = 0;

        // Log() 함수용 플래그 변수
        UInt16 Log_cnt = 0;
        bool flag_Log_make = false;
        bool flag_Log_title = false;
        string FilePath_copy;
        FileInfo fi_copy;

        // 타이머 변수
        Int16 tick = 0;
        int tick1;
        int tick2;

        // 수신 패킷 카운트
        int total_pkt = 0;

        // STX 검사 플래그
        bool stx_flag = false;

        // ID 검사 플래그
        bool ID_flag = false;

        Int16 total_tx = 0;
        Int16 total_rx = 0;
        Int16 total_rx_test = 0;

        // 패킷 판별/분석용 변수

        string[] buffer = new string[50];
        byte[] buffer_pck = new byte[50];
        int pkt_len = 0;
        int temp_len = 0;

        public Form1()
        {
            InitializeComponent();
            LoadListboxes();            // Port, Baudrate 리스트
            LoadIDbox();                // ID 리스트
            LoadSen();                  // 측정 감도 리스트

            // Form 닫을 때 이벤트용
            this.FormClosing += Form1_FormClosing;

            // 이미지 표시
            Logo_box.SizeMode = PictureBoxSizeMode.StretchImage;
            Logo_box.Image = System.Drawing.Image.FromFile(Application.StartupPath + @"\Logo_image\HDC_1_eng.png");
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            btn_open.Enabled = true;
            btn_close.Enabled = false;
            CheckForIllegalCrossThreadCalls = false;
        }

        #region Load Port/Baudrate List
        private void LoadListboxes()
        {
            //1) Available Ports:    
            string[] ports = SerialPort.GetPortNames();

            combo_port.BeginUpdate();
            foreach (string port in ports)
            {
                combo_port.Items.Add(port);
            }
            combo_port.EndUpdate();

            //combo_port.SelectedIndex = 0;

            //2) Baudrates:
            string[] baudrates = { "9600", "14400", "19200", "38400", "57600", "115200" };

            foreach (string baudrate in baudrates)
            {
                combo_br.Items.Add(baudrate);
            }
            combo_br.SelectedIndex = 0;
        }
        #endregion

        #region Load ID List
        private void LoadIDbox()
        {
            foreach (string ID in ID_list)
            {
                combo_ID.Items.Add(ID);
            }
            combo_ID.SelectedIndex = 0;
        }
        #endregion

        #region Load Sensitivity
        private void LoadSen()
        {
            string[] sensitivities = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" };
            foreach (string sen in sensitivities)
            {
                combo_sen.Items.Add(sen);
            }
            combo_sen.SelectedIndex = 2;
        }
        #endregion

        //================ 버튼 로드 ======================
        #region Button Open
        private void btn_open_Click(object sender, EventArgs e)
        {
            if (null == sPort)
            {
                if (combo_port.SelectedItem == null)
                {
                    txt_read.AppendText("포트를 선택하십시오.\r\n");
                    return;
                }
                else
                {
                    sPort = new SerialPort();
                    sPort.DataReceived += new SerialDataReceivedEventHandler(sPort_DataReceived);
                    sPort.PortName = combo_port.SelectedItem.ToString();
                    sPort.BaudRate = Convert.ToInt32(combo_br.Text);
                    sPort.DataBits = (int)8;
                    sPort.Parity = Parity.None;
                    sPort.StopBits = StopBits.One;
                    // 읽기 및 쓰기 제한시간 설정
                    //sPort.ReadTimeout = (int)1000;
                    //sPort.WriteTimeout = (int)1000;

                    try
                    {
                        sPort.Open();
                    }
                    catch (Exception ecpt)
                    {
                        txt_read.AppendText("포트가 다른 곳에서 사용 중입니다.\r\n");
                        return;
                    }
                    finally
                    {
                        btn_open.Enabled = true;
                        btn_close.Enabled = true;
                    }
                }
            }

            if (sPort.IsOpen)
            {
                //-------------- '1A'를 eof가 아닌 char로 받기 위한 코드 --------------
                // 소스 코드 위치: BaseStream은 serialport가 열렸을 때만 사용 가능함(sPort.IsOpen)
                var _handle = (SafeFileHandle)sPort.BaseStream.GetType()
                              .GetField("_handle", BindingFlags.NonPublic | BindingFlags.Instance)
                              .GetValue(sPort.BaseStream);
                SetCommMask(_handle, 0x1F9);
                //--------------------------------------------------------------------

                btn_open.Enabled = false;
                btn_close.Enabled = true;
                timer.Start();

                txt_read.AppendText("연결되었습니다.\r\n");
            }
            else
            {
                btn_open.Enabled = true;
                btn_close.Enabled = false;
            }
        }
        #endregion

        #region Button Close
        private void btn_close_Click(object sender, EventArgs e)
        {
            if (null != sPort)
            {
                if (sPort.IsOpen)
                {
                    sPort.Close();
                    sPort.Dispose();
                    sPort = null;
                }
            }
            btn_open.Enabled = true;
            btn_close.Enabled = false;

            timer.Stop();
            box_color_reset();

            txt_read.AppendText("연결이 끊겼습니다.\r\n");
        }
        #endregion

        #region Button Clear
        private void btn_clear_Click_1(object sender, EventArgs e)
        {
            #region 시간 클리어
            txt_time_1.Clear();
            txt_time_2.Clear();
            txt_time_3.Clear();
            txt_time_4.Clear();
            txt_time_5.Clear();
            txt_time_6.Clear();
            txt_time_7.Clear();
            txt_time_8.Clear();
            txt_time_9.Clear();
            txt_time_10.Clear();
            #endregion

            // 평균값/ 단계값 
            txt_factor1.Clear();
            txt_factor0.Clear();

            #region 평균값 클리어
            txt_avr_1.Clear();
            txt_avr_2.Clear();
            txt_avr_3.Clear();
            txt_avr_4.Clear();
            txt_avr_5.Clear();
            txt_avr_6.Clear();
            txt_avr_7.Clear();
            txt_avr_8.Clear();
            txt_avr_9.Clear();
            txt_avr_10.Clear();
            #endregion

            #region 소음값 클리어
            txt_noi_1.Clear();
            txt_noi_2.Clear();
            txt_noi_3.Clear();
            txt_noi_4.Clear();
            txt_noi_5.Clear();
            txt_noi_6.Clear();
            txt_noi_7.Clear();
            txt_noi_8.Clear();
            txt_noi_9.Clear();
            txt_noi_10.Clear();
            #endregion


            #region 단계 클리어
            txt_state_1.Clear();
            txt_state_2.Clear();
            txt_state_3.Clear();
            txt_state_4.Clear();
            txt_state_5.Clear();
            txt_state_6.Clear();
            txt_state_7.Clear();
            txt_state_8.Clear();
            txt_state_9.Clear();
            txt_state_10.Clear();
            #endregion

            #region ID 클리어
            ID_1.Clear();
            ID_2.Clear();
            ID_3.Clear();
            ID_4.Clear();
            ID_5.Clear();
            ID_6.Clear();
            ID_7.Clear();
            ID_8.Clear();
            ID_9.Clear();
            ID_10.Clear();
            #endregion

            #region 주파수(Frequency) 클리어
            txt_freq1_1.Clear();
            txt_freq1_2.Clear();
            txt_freq1_3.Clear();
            txt_freq1_4.Clear();
            txt_freq1_5.Clear();
            txt_freq1_6.Clear();
            txt_freq1_7.Clear();
            txt_freq1_8.Clear();
            txt_freq1_9.Clear();
            txt_freq1_10.Clear();

            txt_freq2_1.Clear();
            txt_freq2_2.Clear();
            txt_freq2_3.Clear();
            txt_freq2_4.Clear();
            txt_freq2_5.Clear();
            txt_freq2_6.Clear();
            txt_freq2_7.Clear();
            txt_freq2_8.Clear();
            txt_freq2_9.Clear();
            txt_freq2_10.Clear();

            txt_freq3_1.Clear();
            txt_freq3_2.Clear();
            txt_freq3_3.Clear();
            txt_freq3_4.Clear();
            txt_freq3_5.Clear();
            txt_freq3_6.Clear();
            txt_freq3_7.Clear();
            txt_freq3_8.Clear();
            txt_freq3_9.Clear();
            txt_freq3_10.Clear();

            txt_freq4_1.Clear();
            txt_freq4_2.Clear();
            txt_freq4_3.Clear();
            txt_freq4_4.Clear();
            txt_freq4_5.Clear();
            txt_freq4_6.Clear();
            txt_freq4_7.Clear();
            txt_freq4_8.Clear();
            txt_freq4_9.Clear();
            txt_freq4_10.Clear();

            txt_freq5_1.Clear();
            txt_freq5_2.Clear();
            txt_freq5_3.Clear();
            txt_freq5_4.Clear();
            txt_freq5_5.Clear();
            txt_freq5_6.Clear();
            txt_freq5_7.Clear();
            txt_freq5_8.Clear();
            txt_freq5_9.Clear();
            txt_freq5_10.Clear();
            #endregion

            #region 크기(Amplitude) 클리어
            txt_amp1_1.Clear();
            txt_amp1_2.Clear();
            txt_amp1_3.Clear();
            txt_amp1_4.Clear();
            txt_amp1_5.Clear();
            txt_amp1_6.Clear();
            txt_amp1_7.Clear();
            txt_amp1_8.Clear();
            txt_amp1_9.Clear();
            txt_amp1_10.Clear();

            txt_amp2_1.Clear();
            txt_amp2_2.Clear();
            txt_amp2_3.Clear();
            txt_amp2_4.Clear();
            txt_amp2_5.Clear();
            txt_amp2_6.Clear();
            txt_amp2_7.Clear();
            txt_amp2_8.Clear();
            txt_amp2_9.Clear();
            txt_amp2_10.Clear();

            txt_amp3_1.Clear();
            txt_amp3_2.Clear();
            txt_amp3_3.Clear();
            txt_amp3_4.Clear();
            txt_amp3_5.Clear();
            txt_amp3_6.Clear();
            txt_amp3_7.Clear();
            txt_amp3_8.Clear();
            txt_amp3_9.Clear();
            txt_amp3_10.Clear();

            txt_amp4_1.Clear();
            txt_amp4_2.Clear();
            txt_amp4_3.Clear();
            txt_amp4_4.Clear();
            txt_amp4_5.Clear();
            txt_amp4_6.Clear();
            txt_amp4_7.Clear();
            txt_amp4_8.Clear();
            txt_amp4_9.Clear();
            txt_amp4_10.Clear();

            txt_amp5_1.Clear();
            txt_amp5_2.Clear();
            txt_amp5_3.Clear();
            txt_amp5_4.Clear();
            txt_amp5_5.Clear();
            txt_amp5_6.Clear();
            txt_amp5_7.Clear();
            txt_amp5_8.Clear();
            txt_amp5_9.Clear();
            txt_amp5_10.Clear();
            #endregion

            // Status box clear
            box_color_reset();

            // RX-Packet Viewer
            txt_read.Clear();

            // TX-Packet Viewer
            text_view.Clear();

            tx_cnt.Clear();
            rx_cnt.Clear();

            total_tx = 0;
            total_rx = 0;
            cnt_val = 0;

            sPort.DiscardOutBuffer();
            sPort.DiscardInBuffer();
        }
        #endregion

        #region Button Send
        private void btn_send_Click(object sender, EventArgs e)
        {
            // 폴링 플래그 false
            flag_next_pol = false;

            if (sPort == null)
            {
                txt_read.AppendText("Open 버튼을 눌러주세요.\r\n");
            }
            else
            {
                // ID 선택
                for (int i = 0; i < ID_list.Length; i++)
                {
                    if (ID_list[i] == combo_ID.Text)
                    {
                        TxBuffer[1] = ID_list_hex[i];
                    }
                }

                // CMD: default 모드
                TxBuffer[3] = (byte)0xC0;

                // Sequence : 패킷의 일련 번호
                TxBuffer[4] = (byte)seq;
                seq++;
                if (seq > 255)
                {
                    seq = 0;
                }

                // Check Sum 설정
                TxBuffer[5] = Check_Byte(TxBuffer);

                // 패킷 전송
                sPort.Write(TxBuffer, 0, TxBuffer.Length);

                for (int j = 0; j < 6; j++)
                {
                    text_view.AppendText(TxBuffer[j].ToString("X2") + " ");
                }
                text_view.AppendText("\r\n");

                // 토탈 TX
                total_tx++;
                if (tx_cnt.Text.Length != 0)
                {
                    tx_cnt.Clear();
                }
                tx_cnt.AppendText(total_tx.ToString());
            }
        }
        #endregion

        #region Button Set
        private void btn_set_Click(object sender, EventArgs e)
        {
            if (sPort == null)
            {
                txt_read.AppendText("Open 버튼을 눌러주세요.\r\n");
            }
            else
            {
                // ID 선택
                for (int i = 0; i < ID_list.Length; i++)
                {
                    if (ID_list[i] == combo_ID.Text)
                    {
                        TxBuffer[1] = ID_list_hex[i];
                    }
                }

                // CMD: C5
                TxBuffer[3] = (byte)0xC5;

                // Sequence : 여기서는 감도를 표시
                TxBuffer[4] = sen_hex[combo_sen.SelectedIndex];

                // Check Sum 설정
                TxBuffer[5] = Check_Byte(TxBuffer);

                // 패킷 전송
                sPort.Write(TxBuffer, 0, TxBuffer.Length);

                for (int j = 0; j < 6; j++)
                {
                    text_view.AppendText(TxBuffer[j].ToString("X2") + " ");
                }
                text_view.AppendText("\r\n");

                // 토탈 TX
                total_tx++;
                if (tx_cnt.Text.Length != 0)
                {
                    tx_cnt.Clear();
                }
                tx_cnt.AppendText(total_tx.ToString());
            }
        }
        #endregion

        #region Button Polling
        private void btn_stop_Click(object sender, EventArgs e)
        {
            if (btn_stop.Text == "폴링 정지")
            {
                btn_stop.Text = "폴링 시작";
                flag_stop = true;
                flag_start = false;
                flag_next_pol = false;
                //text_view.AppendText("flag_stop: " + flag_stop + "\r\n");
            }
            else if (btn_stop.Text == "폴링 시작")
            {
                btn_stop.Text = "폴링 정지";
                flag_stop = false;
                flag_start = true;
                flag_next_pol = true;
                timer.Start();
                //text_view.AppendText("flag_stop: " + flag_stop + "\r\n");
            }
        }
        #endregion

        //================ 컬러 박스 리셋 =================
        #region box_color_reset
        void box_color_reset()
        {
            st_01.BackColor = Color.WhiteSmoke;
            st_02.BackColor = Color.WhiteSmoke;
            st_03.BackColor = Color.WhiteSmoke;
            st_04.BackColor = Color.WhiteSmoke;
            st_05.BackColor = Color.WhiteSmoke;
            st_06.BackColor = Color.WhiteSmoke;
            st_07.BackColor = Color.WhiteSmoke;
            st_08.BackColor = Color.WhiteSmoke;
            st_09.BackColor = Color.WhiteSmoke;
            st_10.BackColor = Color.WhiteSmoke;
        }
        #endregion

        //================ 패킷 관련 ======================
        #region calc_hex : 주파수, 크기 구하기 및 Log 생성
        void calc_hex(string[] HEX_str)
        {
            // Frequency & Amplitude & 평균값 계산용 변수
            decimal freq_tmp_1, freq_tmp_2;
            decimal amp_tmp_1, amp_tmp_2;
            decimal dB_tmp1, dB_tmp2;
            decimal state;
            decimal amp_result, freq_result, dB_result;
            decimal noise;

            // Factor0, 1 계산용 변수
            decimal fct0_tmp_1, fct0_tmp_2, fct0_tmp_3;
            decimal fct1_tmp_1, fct1_tmp_2, fct1_tmp_3;
            decimal fct0_result, fct1_result;

            if (TxBuffer[3] != (byte)0xC2)  // CMD가 MODE1 일 때
            {
                // HEX를 DEC로 전환 (Frequency)
                freq_tmp_1 = Convert.ToInt32(HEX_str[5], 16);   //소수 부분
                freq_tmp_2 = Convert.ToInt32(HEX_str[6], 16);   //정수 부분
                freq_result = freq_tmp_2 + (freq_tmp_1 / 1000);
                freq_group[cnt_val] = freq_result;               //decimal 버젼
                freq_Log[cnt_val] = string.Format("{0:N2}", freq_result);       //string 버젼

                // HEX를 DEC로 전환 (Amplitude)
                amp_tmp_1 = Convert.ToInt32(HEX_str[7], 16);    //소수 부분
                amp_tmp_2 = Convert.ToInt32(HEX_str[8], 16);    //정수 부분
                amp_result = amp_tmp_2 + (amp_tmp_1 / 1000);
                amp_group[cnt_val] = amp_result * 10;          //decimal 버젼
                amp_Log[cnt_val] = string.Format("{0:N2}", amp_result * 10);    //string 버젼

                // HEX를 DEC로 전환 (dB)
                dB_tmp1 = Convert.ToInt32(HEX_str[9], 16);     //소수 부분
                dB_tmp2 = Convert.ToInt32(HEX_str[10], 16);    //정수 부분
                dB_result = dB_tmp2 + (dB_tmp1 / 100);         //decimal 버젼

                // HEX를 DEC로 전환 (State)
                state = Convert.ToInt32(HEX_str[11], 16);      //정수 부분

                // HEX를 DEC로 전환 (Noise)
                noise = Convert.ToInt32(HEX_str[12], 16);      //정수 부분

                cnt_val++;                                     // 패킷 받을 때마다 카운트
                if (cnt_val == 5)                              // 패킷을 총 5번 받으면 Form에 데이터 라인 업데이트 & Log 표시
                {
                    // 패킷 카운트 초기화
                    cnt_val = 0;

                    // Rx viewer에 한줄 띄기
                    txt_read.AppendText("\n");

                    // csv파일 생성
                    Log(buffer[1], amp_Log, freq_Log, dB_result, state, noise);

                    // 데이터 표시
                    show_data(amp_group, freq_group, dB_result, state, noise);
                }
                else if (cnt_val > 5)
                {
                    txt_read.AppendText("중단, 재시도");
                    cnt_val = 0;
                }
            }
            else if (TxBuffer[3] == (byte)0xC2)  // CMD가 Mode2일 때
            {
                TxBuffer[3] = (byte)0xC1;   //Mode1로 초기화

                // HEX를 DEC로 전환 (factor 0)
                fct0_tmp_1 = Convert.ToInt32(HEX_str[5], 16);   //소수2 부분
                fct0_tmp_2 = Convert.ToInt32(HEX_str[6], 16);   //소수1 부분
                fct0_tmp_3 = Convert.ToInt32(HEX_str[7], 16);   //정수 부분
                fct0_result = fct0_tmp_3 + (fct0_tmp_2 * 256 + fct0_tmp_1) / 10000;

                // HEX를 DEC로 전환 (factor 1)
                fct1_tmp_1 = Convert.ToInt32(HEX_str[8], 16);    //소수2 부분
                fct1_tmp_2 = Convert.ToInt32(HEX_str[9], 16);    //소수1 부분
                fct1_tmp_3 = Convert.ToInt32(HEX_str[10], 16);    //정수 부분
                fct1_result = fct1_tmp_3 + (fct1_tmp_2 * 256 + fct1_tmp_1) / 10000;

                // factor 데이터 표시
                txt_factor0.AppendText(string.Format("{0:N4}", fct0_result) + "\n");       // factor 0 표시
                txt_factor1.AppendText(string.Format("{0:N3}", fct1_result) + "\n");       // factor 1 표시
                //txt_factor0.AppendText(fct0_tmp_3 + " " + fct0_tmp_2 + " " + fct0_tmp_1 + "\n");       // factor 0 표시
                //txt_factor1.AppendText(fct1_tmp_3 + " " + fct1_tmp_2 + " " + fct1_tmp_1 + "\n");       // factor 1 표시
            }
        }
        #endregion

        #region Show data : 데이터 박스 표시(시간/ID/크기/주파수/컬러박스)
        void show_data(decimal[] amp_dec, decimal[] freq_dec, decimal dB, decimal level, decimal noise)
        {
            // 현재 시간 구하기
            DateTime now_time = DateTime.Now;

            // Decimal -> String 변환
            amp_val_1 = string.Format("{0:N2}", amp_dec[0]);
            amp_val_2 = string.Format("{0:N2}", amp_dec[1]);
            amp_val_3 = string.Format("{0:N2}", amp_dec[2]);
            amp_val_4 = string.Format("{0:N2}", amp_dec[3]);
            amp_val_5 = string.Format("{0:N2}", amp_dec[4]);

            freq_val_1 = string.Format("{0:N2}", freq_dec[0]);
            freq_val_2 = string.Format("{0:N2}", freq_dec[1]);
            freq_val_3 = string.Format("{0:N2}", freq_dec[2]);
            freq_val_4 = string.Format("{0:N2}", freq_dec[3]);
            freq_val_5 = string.Format("{0:N2}", freq_dec[4]);

            // 업데이트 할 때마다 줄 바뀜
            switch (next_box)
            {
                #region 첫번째 줄
                case 1:
                    if (txt_time_1.Text.Length != 0)
                    {
                        txt_time_1.Clear();
                        ID_1.Clear();
                        txt_avr_1.Clear();
                        txt_noi_1.Clear();
                        txt_state_1.Clear();

                        txt_freq1_1.Clear();
                        txt_amp1_1.Clear();
                        txt_freq2_1.Clear();
                        txt_amp2_1.Clear();
                        txt_freq3_1.Clear();
                        txt_amp3_1.Clear();
                        txt_freq4_1.Clear();
                        txt_amp4_1.Clear();
                        txt_freq5_1.Clear();
                        txt_amp5_1.Clear();
                    }

                    txt_time_1.AppendText(now_time.ToString("tt HH:mm:ss"));
                    ID_1.AppendText(buffer[1]);                 // ID 표시
                    txt_avr_1.AppendText(dB.ToString());        // 평균 크기 표시
                    txt_noi_1.AppendText(noise.ToString());       // 소음값 표시
                    txt_state_1.AppendText(level.ToString());   // 단계 표시

                    txt_freq1_1.AppendText(freq_val_1);    // 주파수1 표시
                    txt_amp1_1.AppendText(amp_val_1);      // 크기1 표시
                    txt_freq2_1.AppendText(freq_val_2);    // 주파수2 표시
                    txt_amp2_1.AppendText(amp_val_2);      // 크기2 표시
                    txt_freq3_1.AppendText(freq_val_3);    // 주파수3 표시
                    txt_amp3_1.AppendText(amp_val_3);      // 크기3 표시
                    txt_freq4_1.AppendText(freq_val_4);    // 주파수4 표시
                    txt_amp4_1.AppendText(amp_val_4);      // 크기4 표시
                    txt_freq5_1.AppendText(freq_val_5);    // 주파수5 표시
                    txt_amp5_1.AppendText(amp_val_5);      // 크기5 표시

                    st_10.BackColor = Color.WhiteSmoke;         // 이전 단계 회색으로 초기화
                    st_01.BackColor = Color.LimeGreen;          // 현재 순서 라임색으로 표시
                    break;
                #endregion

                #region 두번째 줄
                case 2:
                    if (txt_time_2.Text.Length != 0)
                    {
                        txt_time_2.Clear();
                        ID_2.Clear();
                        txt_avr_2.Clear();
                        txt_state_2.Clear();
                        txt_noi_2.Clear();

                        txt_freq1_2.Clear();
                        txt_amp1_2.Clear();
                        txt_freq2_2.Clear();
                        txt_amp2_2.Clear();
                        txt_freq3_2.Clear();
                        txt_amp3_2.Clear();
                        txt_freq4_2.Clear();
                        txt_amp4_2.Clear();
                        txt_freq5_2.Clear();
                        txt_amp5_2.Clear();
                    }

                    txt_time_2.AppendText(now_time.ToString("tt HH:mm:ss"));
                    ID_2.AppendText(buffer[1]);                 // ID 표시
                    txt_avr_2.AppendText(dB.ToString());        // 평균 크기 표시
                    txt_noi_2.AppendText(noise.ToString());     // 소음값 표시
                    txt_state_2.AppendText(level.ToString());   // 단계 표시
                    txt_freq1_2.AppendText(freq_val_1);    // 주파수1 표시
                    txt_amp1_2.AppendText(amp_val_1);      // 크기1 표시
                    txt_freq2_2.AppendText(freq_val_2);    // 주파수2 표시
                    txt_amp2_2.AppendText(amp_val_2);      // 크기2 표시
                    txt_freq3_2.AppendText(freq_val_3);    // 주파수3 표시
                    txt_amp3_2.AppendText(amp_val_3);      // 크기3 표시
                    txt_freq4_2.AppendText(freq_val_4);    // 주파수4 표시
                    txt_amp4_2.AppendText(amp_val_4);      // 크기4 표시
                    txt_freq5_2.AppendText(freq_val_5);    // 주파수5 표시
                    txt_amp5_2.AppendText(amp_val_5);      // 크기5 표시

                    st_01.BackColor = Color.WhiteSmoke;         // 이전 단계 회색으로 초기화
                    st_02.BackColor = Color.LimeGreen;          // 현재 순서 라임색으로 표시
                    break;
                #endregion

                #region 세번째 줄
                case 3:
                    if (txt_time_3.Text.Length != 0)
                    {
                        txt_time_3.Clear();
                        ID_3.Clear();
                        txt_avr_3.Clear();
                        txt_state_3.Clear();
                        txt_noi_3.Clear();

                        txt_freq1_3.Clear();
                        txt_amp1_3.Clear();
                        txt_freq2_3.Clear();
                        txt_amp2_3.Clear();
                        txt_freq3_3.Clear();
                        txt_amp3_3.Clear();
                        txt_freq4_3.Clear();
                        txt_amp4_3.Clear();
                        txt_freq5_3.Clear();
                        txt_amp5_3.Clear();
                    }

                    txt_time_3.AppendText(now_time.ToString("tt HH:mm:ss"));
                    ID_3.AppendText(buffer[1]);                 // ID 표시
                    txt_avr_3.AppendText(dB.ToString());        // 평균 크기 표시
                    txt_noi_3.AppendText(noise.ToString());     // 소음값 표시
                    txt_state_3.AppendText(level.ToString());   // 단계 표시
                    txt_freq1_3.AppendText(freq_val_1);    // 주파수1 표시
                    txt_amp1_3.AppendText(amp_val_1);      // 크기1 표시
                    txt_freq2_3.AppendText(freq_val_2);    // 주파수2 표시
                    txt_amp2_3.AppendText(amp_val_2);      // 크기2 표시
                    txt_freq3_3.AppendText(freq_val_3);    // 주파수3 표시
                    txt_amp3_3.AppendText(amp_val_3);      // 크기3 표시
                    txt_freq4_3.AppendText(freq_val_4);    // 주파수4 표시
                    txt_amp4_3.AppendText(amp_val_4);      // 크기4 표시
                    txt_freq5_3.AppendText(freq_val_5);    // 주파수5 표시
                    txt_amp5_3.AppendText(amp_val_5);      // 크기5 표시

                    st_02.BackColor = Color.WhiteSmoke;         // 이전 단계 회색으로 초기화
                    st_03.BackColor = Color.LimeGreen;          // 현재 순서 라임색으로 표시
                    break;
                #endregion

                #region 네번째 줄
                case 4:
                    if (txt_time_4.Text.Length != 0)
                    {
                        txt_time_4.Clear();
                        ID_4.Clear();
                        txt_avr_4.Clear();
                        txt_state_4.Clear();
                        txt_noi_4.Clear();

                        txt_freq1_4.Clear();
                        txt_amp1_4.Clear();
                        txt_freq2_4.Clear();
                        txt_amp2_4.Clear();
                        txt_freq3_4.Clear();
                        txt_amp3_4.Clear();
                        txt_freq4_4.Clear();
                        txt_amp4_4.Clear();
                        txt_freq5_4.Clear();
                        txt_amp5_4.Clear();
                    }

                    txt_time_4.AppendText(now_time.ToString("tt HH:mm:ss"));
                    ID_4.AppendText(buffer[1]);                 // ID 표시
                    txt_avr_4.AppendText(dB.ToString());        // 평균 크기 표시
                    txt_noi_4.AppendText(noise.ToString());     // 소음값 표시
                    txt_state_4.AppendText(level.ToString());   // 단계 표시
                    txt_freq1_4.AppendText(freq_val_1);    // 주파수1 표시
                    txt_amp1_4.AppendText(amp_val_1);      // 크기1 표시
                    txt_freq2_4.AppendText(freq_val_2);    // 주파수2 표시
                    txt_amp2_4.AppendText(amp_val_2);      // 크기2 표시
                    txt_freq3_4.AppendText(freq_val_3);    // 주파수3 표시
                    txt_amp3_4.AppendText(amp_val_3);      // 크기3 표시
                    txt_freq4_4.AppendText(freq_val_4);    // 주파수4 표시
                    txt_amp4_4.AppendText(amp_val_4);      // 크기4 표시
                    txt_freq5_4.AppendText(freq_val_5);    // 주파수5 표시
                    txt_amp5_4.AppendText(amp_val_5);      // 크기5 표시

                    st_03.BackColor = Color.WhiteSmoke;         // 이전 단계 회색으로 초기화
                    st_04.BackColor = Color.LimeGreen;          // 현재 순서 라임색으로 표시
                    break;
                #endregion

                #region 다섯번째 줄
                case 5:
                    if (txt_time_5.Text.Length != 0)
                    {
                        txt_time_5.Clear();
                        ID_5.Clear();
                        txt_avr_5.Clear();
                        txt_state_5.Clear();
                        txt_noi_5.Clear();

                        txt_freq1_5.Clear();
                        txt_amp1_5.Clear();
                        txt_freq2_5.Clear();
                        txt_amp2_5.Clear();
                        txt_freq3_5.Clear();
                        txt_amp3_5.Clear();
                        txt_freq4_5.Clear();
                        txt_amp4_5.Clear();
                        txt_freq5_5.Clear();
                        txt_amp5_5.Clear();
                    }

                    txt_time_5.AppendText(now_time.ToString("tt HH:mm:ss"));
                    ID_5.AppendText(buffer[1]);                 // ID 표시
                    txt_avr_5.AppendText(dB.ToString());        // 평균 크기 표시
                    txt_noi_5.AppendText(noise.ToString());     // 소음값 표시
                    txt_state_5.AppendText(level.ToString());   // 단계 표시
                    txt_freq1_5.AppendText(freq_val_1);    // 주파수1 표시
                    txt_amp1_5.AppendText(amp_val_1);      // 크기1 표시
                    txt_freq2_5.AppendText(freq_val_2);    // 주파수2 표시
                    txt_amp2_5.AppendText(amp_val_2);      // 크기2 표시
                    txt_freq3_5.AppendText(freq_val_3);    // 주파수3 표시
                    txt_amp3_5.AppendText(amp_val_3);      // 크기3 표시
                    txt_freq4_5.AppendText(freq_val_4);    // 주파수4 표시
                    txt_amp4_5.AppendText(amp_val_4);      // 크기4 표시
                    txt_freq5_5.AppendText(freq_val_5);    // 주파수5 표시
                    txt_amp5_5.AppendText(amp_val_5);      // 크기5 표시

                    st_04.BackColor = Color.WhiteSmoke;         // 이전 단계 회색으로 초기화
                    st_05.BackColor = Color.LimeGreen;          // 현재 순서 라임색으로 표시
                    break;
                #endregion

                #region 여섯번째 줄
                case 6:
                    if (txt_time_6.Text.Length != 0)
                    {
                        txt_time_6.Clear();
                        ID_6.Clear();
                        txt_avr_6.Clear();
                        txt_state_6.Clear();
                        txt_noi_6.Clear();

                        txt_freq1_6.Clear();
                        txt_amp1_6.Clear();
                        txt_freq2_6.Clear();
                        txt_amp2_6.Clear();
                        txt_freq3_6.Clear();
                        txt_amp3_6.Clear();
                        txt_freq4_6.Clear();
                        txt_amp4_6.Clear();
                        txt_freq5_6.Clear();
                        txt_amp5_6.Clear();
                    }

                    txt_time_6.AppendText(now_time.ToString("tt HH:mm:ss"));
                    ID_6.AppendText(buffer[1]);                 // ID 표시
                    txt_avr_6.AppendText(dB.ToString());        // 평균 크기 표시
                    txt_noi_6.AppendText(noise.ToString());     // 소음값 표시
                    txt_state_6.AppendText(level.ToString());   // 단계 표시
                    txt_freq1_6.AppendText(freq_val_1);    // 주파수1 표시
                    txt_amp1_6.AppendText(amp_val_1);      // 크기1 표시
                    txt_freq2_6.AppendText(freq_val_2);    // 주파수2 표시
                    txt_amp2_6.AppendText(amp_val_2);      // 크기2 표시
                    txt_freq3_6.AppendText(freq_val_3);    // 주파수3 표시
                    txt_amp3_6.AppendText(amp_val_3);      // 크기3 표시
                    txt_freq4_6.AppendText(freq_val_4);    // 주파수4 표시
                    txt_amp4_6.AppendText(amp_val_4);      // 크기4 표시
                    txt_freq5_6.AppendText(freq_val_5);    // 주파수5 표시
                    txt_amp5_6.AppendText(amp_val_5);      // 크기5 표시

                    st_05.BackColor = Color.WhiteSmoke;         // 이전 단계 회색으로 초기화
                    st_06.BackColor = Color.LimeGreen;          // 현재 순서 라임색으로 표시
                    break;
                #endregion

                #region 일곱번째 줄
                case 7:
                    if (txt_time_7.Text.Length != 0)
                    {
                        txt_time_7.Clear();
                        ID_7.Clear();
                        txt_avr_7.Clear();
                        txt_state_7.Clear();
                        txt_noi_7.Clear();

                        txt_freq1_7.Clear();
                        txt_amp1_7.Clear();
                        txt_freq2_7.Clear();
                        txt_amp2_7.Clear();
                        txt_freq3_7.Clear();
                        txt_amp3_7.Clear();
                        txt_freq4_7.Clear();
                        txt_amp4_7.Clear();
                        txt_freq5_7.Clear();
                        txt_amp5_7.Clear();
                    }

                    txt_time_7.AppendText(now_time.ToString("tt HH:mm:ss"));
                    ID_7.AppendText(buffer[1]);                 // ID 표시
                    txt_avr_7.AppendText(dB.ToString());        // 평균 크기 표시
                    txt_noi_7.AppendText(noise.ToString());     // 소음값 표시
                    txt_state_7.AppendText(level.ToString());   // 단계 표시
                    txt_freq1_7.AppendText(freq_val_1);    // 주파수1 표시
                    txt_amp1_7.AppendText(amp_val_1);      // 크기1 표시
                    txt_freq2_7.AppendText(freq_val_2);    // 주파수2 표시
                    txt_amp2_7.AppendText(amp_val_2);      // 크기2 표시
                    txt_freq3_7.AppendText(freq_val_3);    // 주파수3 표시
                    txt_amp3_7.AppendText(amp_val_3);      // 크기3 표시
                    txt_freq4_7.AppendText(freq_val_4);    // 주파수4 표시
                    txt_amp4_7.AppendText(amp_val_4);      // 크기4 표시
                    txt_freq5_7.AppendText(freq_val_5);    // 주파수5 표시
                    txt_amp5_7.AppendText(amp_val_5);      // 크기5 표시

                    st_06.BackColor = Color.WhiteSmoke;         // 이전 단계 회색으로 초기화
                    st_07.BackColor = Color.LimeGreen;          // 현재 순서 라임색으로 표시
                    break;
                #endregion

                #region 여덟번째 줄
                case 8:
                    if (txt_time_8.Text.Length != 0)
                    {
                        txt_time_8.Clear();
                        ID_8.Clear();
                        txt_avr_8.Clear();
                        txt_state_8.Clear();
                        txt_noi_8.Clear();

                        txt_freq1_8.Clear();
                        txt_amp1_8.Clear();
                        txt_freq2_8.Clear();
                        txt_amp2_8.Clear();
                        txt_freq3_8.Clear();
                        txt_amp3_8.Clear();
                        txt_freq4_8.Clear();
                        txt_amp4_8.Clear();
                        txt_freq5_8.Clear();
                        txt_amp5_8.Clear();
                    }

                    txt_time_8.AppendText(now_time.ToString("tt HH:mm:ss"));
                    ID_8.AppendText(buffer[1]);                 // ID 표시
                    txt_avr_8.AppendText(dB.ToString());        // 평균 크기 표시
                    txt_noi_8.AppendText(noise.ToString());     // 소음값 표시
                    txt_state_8.AppendText(level.ToString());   // 단계 표시
                    txt_freq1_8.AppendText(freq_val_1);    // 주파수1 표시
                    txt_amp1_8.AppendText(amp_val_1);      // 크기1 표시
                    txt_freq2_8.AppendText(freq_val_2);    // 주파수2 표시
                    txt_amp2_8.AppendText(amp_val_2);      // 크기2 표시
                    txt_freq3_8.AppendText(freq_val_3);    // 주파수3 표시
                    txt_amp3_8.AppendText(amp_val_3);      // 크기3 표시
                    txt_freq4_8.AppendText(freq_val_4);    // 주파수4 표시
                    txt_amp4_8.AppendText(amp_val_4);      // 크기4 표시
                    txt_freq5_8.AppendText(freq_val_5);    // 주파수5 표시
                    txt_amp5_8.AppendText(amp_val_5);      // 크기5 표시

                    st_07.BackColor = Color.WhiteSmoke;         // 이전 단계 회색으로 초기화
                    st_08.BackColor = Color.LimeGreen;          // 현재 순서 라임색으로 표시
                    break;
                #endregion

                #region 아홉번째 줄
                case 9:
                    if (txt_time_9.Text.Length != 0)
                    {
                        txt_time_9.Clear();
                        ID_9.Clear();
                        txt_avr_9.Clear();
                        txt_state_9.Clear();
                        txt_noi_9.Clear();

                        txt_freq1_9.Clear();
                        txt_amp1_9.Clear();
                        txt_freq2_9.Clear();
                        txt_amp2_9.Clear();
                        txt_freq3_9.Clear();
                        txt_amp3_9.Clear();
                        txt_freq4_9.Clear();
                        txt_amp4_9.Clear();
                        txt_freq5_9.Clear();
                        txt_amp5_9.Clear();
                    }

                    txt_time_9.AppendText(now_time.ToString("tt HH:mm:ss"));
                    ID_9.AppendText(buffer[1]);                 // ID 표시
                    txt_avr_9.AppendText(dB.ToString());        // 평균 크기 표시
                    txt_noi_9.AppendText(noise.ToString());     // 소음값 표시
                    txt_state_9.AppendText(level.ToString());   // 단계 표시
                    txt_freq1_9.AppendText(freq_val_1);    // 주파수1 표시
                    txt_amp1_9.AppendText(amp_val_1);      // 크기1 표시
                    txt_freq2_9.AppendText(freq_val_2);    // 주파수2 표시
                    txt_amp2_9.AppendText(amp_val_2);      // 크기2 표시
                    txt_freq3_9.AppendText(freq_val_3);    // 주파수3 표시
                    txt_amp3_9.AppendText(amp_val_3);      // 크기3 표시
                    txt_freq4_9.AppendText(freq_val_4);    // 주파수4 표시
                    txt_amp4_9.AppendText(amp_val_4);      // 크기4 표시
                    txt_freq5_9.AppendText(freq_val_5);    // 주파수5 표시
                    txt_amp5_9.AppendText(amp_val_5);      // 크기5 표시

                    st_08.BackColor = Color.WhiteSmoke;         // 이전 단계 회색으로 초기화
                    st_09.BackColor = Color.LimeGreen;          // 현재 순서 라임색으로 표시
                    break;
                #endregion

                #region 열번째 줄
                case 10:
                    if (txt_time_10.Text.Length != 0)
                    {
                        txt_time_10.Clear();
                        ID_10.Clear();
                        txt_avr_10.Clear();
                        txt_state_10.Clear();
                        txt_noi_10.Clear();

                        txt_freq1_10.Clear();
                        txt_amp1_10.Clear();
                        txt_freq2_10.Clear();
                        txt_amp2_10.Clear();
                        txt_freq3_10.Clear();
                        txt_amp3_10.Clear();
                        txt_freq4_10.Clear();
                        txt_amp4_10.Clear();
                        txt_freq5_10.Clear();
                        txt_amp5_10.Clear();
                    }

                    txt_time_10.AppendText(now_time.ToString("tt HH:mm:ss"));
                    ID_10.AppendText(buffer[1]);                 // ID 표시
                    txt_avr_10.AppendText(dB.ToString());        // 평균 크기 표시
                    txt_noi_10.AppendText(noise.ToString());      // 소음값 표시
                    txt_state_10.AppendText(level.ToString());   // 단계 표시
                    txt_freq1_10.AppendText(freq_val_1);    // 주파수1 표시
                    txt_amp1_10.AppendText(amp_val_1);      // 크기1 표시
                    txt_freq2_10.AppendText(freq_val_2);    // 주파수2 표시
                    txt_amp2_10.AppendText(amp_val_2);      // 크기2 표시
                    txt_freq3_10.AppendText(freq_val_3);    // 주파수3 표시
                    txt_amp3_10.AppendText(amp_val_3);      // 크기3 표시
                    txt_freq4_10.AppendText(freq_val_4);    // 주파수4 표시
                    txt_amp4_10.AppendText(amp_val_4);      // 크기4 표시
                    txt_freq5_10.AppendText(freq_val_5);    // 주파수5 표시
                    txt_amp5_10.AppendText(amp_val_5);      // 크기5 표시

                    st_09.BackColor = Color.WhiteSmoke;         // 이전 단계 회색으로 초기화
                    st_10.BackColor = Color.LimeGreen;          // 현재 순서 라임색으로 표시
                    break;
                #endregion

                default:
                    break;
            }
        }
        #endregion

        #region Check Sum
        public byte Check_Byte(byte[] packet)//(char[] packet)
        {
            byte i, cbyte = 0x02;

            for (i = 1; i < (packet[2] - 1); i++)
            {
                cbyte ^= packet[i]; // XOR 
                cbyte++; // 1증가
            }

            return cbyte;
        }
        #endregion

        #region Serial Port DataReceived
        void sPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            // 센서 데이터 읽기 및 데이터 출력 
            byte[] pkt_rcv = new byte[100];
            pkt_len = sPort.BytesToRead;    // 읽을 수 있는 바이트 숫자
            sPort.Read(pkt_rcv, 0, pkt_len);     // 읽은 바이트 숫자
            CheckForIllegalCrossThreadCalls = false;

            for (int i = temp_len; i < (temp_len + pkt_len); i++)
            {
                try
                {
                    buffer[i] = String.Format("{0:X2}", Convert.ToInt32(pkt_rcv[i - temp_len]));    // string 버전
                    buffer_pck[i] = pkt_rcv[i - temp_len];  // byte 버전
                    //txt_read.AppendText(buffer[i] + "  ");
                }
                catch (Exception err)
                {
                    txt_read.AppendText("길이를 벗어났습니다.\r\n");
                    //txt_read.AppendText("i: " + i + "      " + "i - temp_len: " + (i - temp_len) + "\r\n");
                }
            }
            temp_len = pkt_len;
            total_pkt = total_pkt + pkt_len;
            //txt_read.AppendText("\r\n");

            //txt_read.AppendText("total_pkt: " + total_pkt + "\r\n");
            // 받은 패킷을 뷰어에 출력 

            Console.WriteLine("len: " + pkt_len + "total:" + total_pkt);

            if (total_pkt >= 14)    // 3바이트 더 추가(평균값/dB 정수, 소수, 단계) + 소음값 추가
            {
                if (total_pkt > 14)
                    total_pkt = 14;
                for (int i = 0; i < total_pkt; i++)
                {
                    txt_read.AppendText(buffer[i] + " ");
                }
                txt_read.AppendText("\r\n");
                // RX 카운트 테스트
                total_rx_test++;
                //text_view.AppendText(total_rx_test.ToString()+"\r\n");

                // 초기화
                temp_len = 0;
                total_pkt = 0;

                // STX 판별 플래그
                if (buffer[0] == "02")
                {
                    stx_flag = true;
                }
                else
                {
                    stx_flag = false;
                }

                // ID 판별 플래그
                //if ((buffer[1] == "0A") || (buffer[1] == "0B") || (buffer[1] == "0C") || (buffer[1] == "0D"))
                if ((buffer[1] == "01") || (buffer[1] == "02") || (buffer[1] == "03") || (buffer[1] == "04"))
                {
                    ID_flag = true;
                }
                else
                {
                    ID_flag = false;
                }
            }


            // ============================= STX와 ID가 맞을 경우  =============================
            if ((stx_flag == true) && (ID_flag == true))
            {
                //txt_read.AppendText("받은 패킷 CS(패킷 내): " + buffer[9] + "\r\n");
                //txt_read.AppendText("받은 패킷 CS(계산): " + Check_Byte(buffer_pck).ToString("X") + "\r\n");

                stx_flag = false;
                ID_flag = false;
                flag_start = false;

                if (buffer_pck[13] == Check_Byte(buffer_pck)) // CS가 일치할 경우
                {
                    // Hex값 -> Decimal형태의 FFT 결과값 계산
                    // 패킷 5개를 한 묶음으로 하여 Form에 표시
                    calc_hex(buffer);

                    // 데이터를 받으면 timer tick 초기화 -> 1초 뒤 로그생성
                    tick = 0;

                    // ID & 측정값 출력
                    switch (buffer[1])
                    {
                        // ID별로 하기에는 너무 길어서 한군데로 통합함.. 
                        case "01":
                        case "02":
                        case "03":
                        case "04":
                            {
                                // 토탈 RX
                                total_rx++;
                                if (rx_cnt.Text.Length != 0)
                                {
                                    rx_cnt.Clear();
                                }
                                rx_cnt.AppendText(total_rx.ToString());

                                // csv파일 생성
                                //Log(buffer[1], freq_Log, amp_Log);
                            }
                            break;

                        default:
                            break;
                    }

                }
                else
                {
                    //txt_read.AppendText("check byte가 맞지 않습니다(2).\r\n");
                    //txt_read.AppendText("buffer_pck[9]: " + buffer_pck[9] + " Check_Byte(buffer_pck): " + Check_Byte(buffer_pck) + "\r\n");

                    /* 센서 읽기 요청 재전송 */
                    text_view.AppendText("요청을 재전송 합니다\r\n");
                    sPort.Write(TxBuffer, 0, TxBuffer.Length);
                }
            }
        }
        #endregion

        //================ Log 관련 =======================
        #region GetDateTime
        public string GetDateTime()
        {
            DateTime NowDate = DateTime.Now;
            return NowDate.ToString("HH:mm:ss");//("yyyy-MM-dd HH:mm:ss");
        }
        #endregion

        #region Log(Id, Amp, Freq, Avr(2~5), Level, noise)
        public void Log(string str1, string[] str2, string[] str3, decimal dB, decimal level, decimal noise)
        {
            /*
            if (flag_Log_make == true)
            {
                flag_Log_make = false;
                
                // 로그 타이틀 번호 생성
                Log_cnt++;
            }
            */

            // 로그 타이틀 번호 생성
            //Log_cnt++;

            //string FilePath = Application.StartupPath + @"\Logs\Log_" + DateTime.Today.ToString("yyyyMMdd") + "_" + Log_cnt.ToString() + ".csv";
            string FilePath = Application.StartupPath + @"\Logs\Log_" + DateTime.Today.ToString("yyyyMMdd") + ".csv";
            string DirPath = Application.StartupPath + @"\Logs";
            string temp;

            DirectoryInfo di = new DirectoryInfo(DirPath);
            FileInfo fi = new FileInfo(FilePath);

            // 파일 경로 복사 -> Form1_FormClosing에 사용
            FilePath_copy = FilePath;
            fi_copy = fi;

            try
            {
                if (di.Exists != true)
                {
                    Directory.CreateDirectory(DirPath);
                }
                if (fi.Exists != true)
                {
                    using (StreamWriter sw = new StreamWriter(FilePath))
                    {
                        //if (flag_Log_title == true)
                        //{
                        flag_Log_title = false;
                        temp = string.Format("{0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}, {12}, {13}, {14}, {15}",
                                             "Date", "Time", "ID", "Amp 1", "Freq 1", "Amp 2", "Freq 2", "Amp 3", "Freq 3", "Amp 4", "Freq 4", "Amp 5", "Freq 5", "Avr(#2~5)", "Level", "Noise(dB)");
                        //"Time", "ID", "Amplitude1", "Frequency1", "Amplitude2", "Frequency2", "Amplitude3", "Frequency3",
                        //"Amplitude4", "Frequency4", "Amplitude5", "Frequency5");
                        sw.WriteLine(temp);
                        //}
                        temp = string.Format("{0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}, {12}, {13}, {14}, {15}",
                                             DateTime.Today.ToString("yy.MM.dd"), GetDateTime(), str1, str2[0], str3[0], str2[1],
                                             str3[1], str2[2], str3[2], str2[3], str3[3], str2[4], str3[4], dB.ToString(), level.ToString(), noise.ToString());
                        sw.WriteLine(temp);
                        sw.Close();
                    }
                }
                else
                {
                    using (StreamWriter sw = File.AppendText(FilePath))
                    {
                        temp = string.Format("{0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}, {12}, {13}, {14}, {15}",
                                             DateTime.Today.ToString("yy.MM.dd"), GetDateTime(), str1, str2[0], str3[0], str2[1], str3[1],
                                             str2[2], str3[2], str2[3], str3[3], str2[4], str3[4], dB.ToString(), level.ToString(), noise.ToString());
                        sw.WriteLine(temp);
                        sw.Close();
                    }
                }
            }
            catch
            {

            }

            DeleteServiceLogByDay(7);

            // 데이터 박스들 다음 순서로 이동
            next_box++;
            if (next_box == 11)
            {
                next_box = 1;   // 열번째 이후에는 다시 첫번째로 돌아가 데이터 박스 업데이트
            }
        }
        #endregion

        #region DeleteServiceLogByDay
        internal void DeleteServiceLogByDay(int keepDay)
        {
            try
            {
                //string FilePath = Application.StartupPath + @"\Logs\Log" + DateTime.Today.ToString("yyyyMMdd") + ".log";
                string DirPath = Application.StartupPath + @"\Logs";

                DirectoryInfo di = new DirectoryInfo(DirPath);

                foreach (DirectoryInfo dir in di.GetDirectories())
                {
                    foreach (FileInfo file in dir.GetFiles())
                    {
                        if (file.Extension != ".csv")
                        {
                            continue;
                        }

                        if (file.CreationTime < DateTime.Now.AddDays(-(keepDay)))
                        {
                            file.Delete();
                        }
                    }
                }

                di = null;
            }
            catch (Exception ex)
            {
                //Log("파일 삭제 에러 발생 = " + ex);
            }
        }
        #endregion

        #region Form종료시 Log에 표시
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            string endline;

            //폼을 닫을건지 취소 할 것인지 결정
            DialogResult dr = MessageBox.Show("종료하시겠습니까?",
            "종료확인",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information);

            if (dr == DialogResult.No)
            {
                e.Cancel = true; //취소
            }
            else
            {
                // Log에 측정 종료한 시점을 ======로 표시
                try
                {
                    if (fi_copy.Exists == true)
                    {
                        using (StreamWriter sw = File.AppendText(FilePath_copy))
                        {
                            endline = string.Format("{0}",
                                                    "==================================================================================================");
                            sw.WriteLine(endline);
                            sw.Close();
                        }
                    }
                }
                catch
                {

                }
            }
        }
        #endregion

        //================ Time 관련 ======================
        # region Delay(milli second)
        private static DateTime Delay(int MS)
        {
            DateTime ThisMoment = DateTime.Now;
            TimeSpan duration = new TimeSpan(0, 0, 0, 0, MS);
            DateTime AfterWards = ThisMoment.Add(duration);
            while (AfterWards >= ThisMoment)
            {
                System.Windows.Forms.Application.DoEvents();
                ThisMoment = DateTime.Now;
            }
            return DateTime.Now;
        }
        #endregion

        #region Timer tick
        private void timer_Tick(object sender, EventArgs e)
        {
            //timer.Interval = 500;   // 타이머 간격 500ms 설정
            tick++;     // 패킷 수신하고 일정시간 뒤에 Log 생성용

            if (tick == 5) // 1초 경과 (timer.Interval*tick = 1000ms)
            {
                // Log 생성하기
                text_view.AppendText("로그를 기록\r\n");

                // 훗날을 위해 남겨두자...패킷 들어올때마다 로그 만들기 용(+ 로그 타이틀 #붙이기)
                flag_Log_make = true;
                flag_Log_title = true;
                flag_start = true;
            }

            /* 폴링용 */
            //if (flag_start == true)
            if ((flag_start == true) && (flag_next_pol == true))
            {
                if (sPort == null)
                {
                    return;
                }
                else
                {
                    TxBuffer[1] = ID_list_hex[ID_pol];
                    // TxBuffer[0], [2]은 STX/LEN로 고정
                    TxBuffer[3] = (byte)0xC0;
                    TxBuffer[4] = (byte)seq;
                    TxBuffer[5] = Check_Byte(TxBuffer);

                    // 조명 제어 요청 패킷 송신
                    sPort.Write(TxBuffer, 0, TxBuffer.Length);

                    for (int j = 0; j < 6; j++)
                    {
                        text_view.AppendText(TxBuffer[j].ToString("X2") + " ");
                    }
                    text_view.AppendText("\r\n");

                    seq++;
                    if (seq > 255)
                    {
                        seq = 0;
                    }

                    ID_pol++;
                    if (ID_pol == ID_list.Length)
                    {
                        ID_pol = 0;
                    }

                    // 토탈 TX
                    total_tx++;
                    if (tx_cnt.Text.Length != 0)
                    {
                        tx_cnt.Clear();
                    }
                    tx_cnt.AppendText(total_tx.ToString());
                }
            }
        }
        #endregion

        #region Modes
        private void btn_mode_1_Click(object sender, EventArgs e)
        {
            // 폴링 플래그 false
            flag_next_pol = false;

            if (sPort == null)
            {
                txt_read.AppendText("Open 버튼을 눌러주세요.\r\n");
            }
            else
            {
                // ID 선택
                for (int i = 0; i < ID_list.Length; i++)
                {
                    if (ID_list[i] == combo_ID.Text)
                    {
                        TxBuffer[1] = ID_list_hex[i];
                    }
                }

                // CMD: 모드 1
                TxBuffer[3] = (byte)0xC1;

                // Sequence : 패킷의 일련 번호
                TxBuffer[4] = (byte)seq;
                seq++;
                if (seq > 255)
                {
                    seq = 0;
                }

                // Check Sum 설정
                TxBuffer[5] = Check_Byte(TxBuffer);

                // 패킷 전송
                sPort.Write(TxBuffer, 0, TxBuffer.Length);

                for (int j = 0; j < 6; j++)
                {
                    text_view.AppendText(TxBuffer[j].ToString("X2") + " ");
                }
                text_view.AppendText("\r\n");

                // 토탈 TX
                total_tx++;
                if (tx_cnt.Text.Length != 0)
                {
                    tx_cnt.Clear();
                }
                tx_cnt.AppendText(total_tx.ToString());
            }
        }

        private void btn_mode_2_Click(object sender, EventArgs e)
        {
            // 폴링 플래그 false
            flag_next_pol = false;

            if (sPort == null)
            {
                txt_read.AppendText("Open 버튼을 눌러주세요.\r\n");
            }
            else
            {
                // ID 선택
                for (int i = 0; i < ID_list.Length; i++)
                {
                    if (ID_list[i] == combo_ID.Text)
                    {
                        TxBuffer[1] = ID_list_hex[i];
                    }
                }

                // CMD: 모드 2
                TxBuffer[3] = (byte)0xC2;

                // Sequence : 패킷의 일련 번호
                TxBuffer[4] = (byte)seq;
                seq++;
                if (seq > 255)
                {
                    seq = 0;
                }

                // Check Sum 설정
                TxBuffer[5] = Check_Byte(TxBuffer);

                // 패킷 전송
                sPort.Write(TxBuffer, 0, TxBuffer.Length);

                for (int j = 0; j < 6; j++)
                {
                    text_view.AppendText(TxBuffer[j].ToString("X2") + " ");
                }
                text_view.AppendText("\r\n");

                // 토탈 TX
                total_tx++;
                if (tx_cnt.Text.Length != 0)
                {
                    tx_cnt.Clear();
                }
                tx_cnt.AppendText(total_tx.ToString());
            }
        }
        #endregion

    }
}
