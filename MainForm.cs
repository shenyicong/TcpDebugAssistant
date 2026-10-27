using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TcpDebugAssistant
{
    /// <summary>
    /// TCP 调试助手主窗体（支持深色 / 浅色主题，参考常见 TCP 调试工具布局）。
    /// 功能：
    ///   1. 服务端模式：监听本机端口，接入多客户端，向指定客户端或广播发送。
    ///   2. 客户端模式：连接远端 IP:端口，支持断线自动重连。
    ///   3. 接收区：文本 / HEX 视图、时间戳、自动滚屏、表格视图、保存日志、清空。
    ///   4. 发送区：ASCII/UTF-8/UTF-16/GBK 编码，HEX 发送、追加 CRLF、循环发送（可设次数）。
    ///   5. 顶部状态条显示连接状态与 TX/RX 统计；右下角可切换深/浅主题。
    /// 界面全部用代码创建，无需设计器。
    /// </summary>
    public partial class MainForm : Form
    {
        // ===================== 状态字段 =====================
        private enum Mode { Server, Client }
        private Mode _mode = Mode.Server;

        private TcpListener _listener;
        private readonly Dictionary<string, TcpClient> _clients = new Dictionary<string, TcpClient>();
        private readonly List<string> _clientKeys = new List<string>();

        private TcpClient _client;
        private NetworkStream _clientStream;
        private CancellationTokenSource _cts;

        private long _recvBytes = 0, _recvPackets = 0, _sendBytes = 0, _sendPackets = 0;
        private readonly System.Windows.Forms.Timer _loopTimer = new System.Windows.Forms.Timer();
        private bool _switching = false;
        private bool _dark = true;            // 当前主题（true=深色）
        private int _loopSent = 0;            // 循环发送已发次数
        private bool _autoReconnect = false;  // 客户端断线自动重连

        private const string SendPlaceholder = "在此输入要发送的内容，按 Enter 或点「发送」…";

        // ===================== 控件声明 =====================
        private RadioButton rdoServer, rdoClient;
        private Label lblLocalPort, lblRemoteIp, lblRemotePort, lblClients, lblOnline, lblClientStatus, lblTxRx, lblTitle;
        private TextBox txtLocalPort, txtRemoteIp, txtRemotePort;
        private Button btnListen, btnConnect, btnSend, btnSaveLog, btnClearRecv, btnDisconnectSel;
        private ListBox lstClients;
        private RichTextBox txtLog, txtSend;
        private CheckBox chkHexView, chkTimestamp, chkAutoScroll, chkSendHex, chkAppendCRLF, chkLoop;
        private CheckBox chkTableView, chkTheme, chkAutoReconnect;
        private NumericUpDown numLoopInterval, numLoopCount;
        private ComboBox cmbEncoding;
        private Panel pnlTitle, pnlStatus, pnlConn, pnlClients, pnlRecv, pnlSend;
        private DataGridView dgvRecv;

        // ===================== 主题色 =====================
        // 深色
        private static readonly Color DkBg = Color.FromArgb(0x15, 0x16, 0x1A);
        private static readonly Color DkPanel = Color.FromArgb(0x1F, 0x20, 0x25);
        private static readonly Color DkPanel2 = Color.FromArgb(0x26, 0x27, 0x2D);
        private static readonly Color DkBorder = Color.FromArgb(0x3A, 0x3B, 0x42);
        private static readonly Color DkText = Color.FromArgb(0xE6, 0xE6, 0xE6);
        private static readonly Color DkSub = Color.FromArgb(0x9A, 0x9A, 0xA2);
        private static readonly Color DkGreen = Color.FromArgb(0x4C, 0xAF, 0x50);
        private static readonly Color DkRed = Color.FromArgb(0xE5, 0x39, 0x35);
        private static readonly Color DkBlue = Color.FromArgb(0x21, 0x96, 0xF3);
        private static readonly Color DkPurple = Color.FromArgb(0x7C, 0x4D, 0xFF);
        private static readonly Color DkPurpleH = Color.FromArgb(0x6A, 0x3D, 0xEE);
        private static readonly Color DkGrayBtn = Color.FromArgb(0x33, 0x34, 0x3B);
        private static readonly Color DkLogRecv = Color.FromArgb(0x66, 0xBB, 0x6A);
        private static readonly Color DkLogSend = Color.FromArgb(0x4F, 0xC3, 0xF7);
        private static readonly Color DkLogSys = Color.FromArgb(0x9E, 0x9E, 0x9E);
        private static readonly Color DkLogErr = Color.FromArgb(0xEF, 0x53, 0x50);
        // 浅色
        private static readonly Color LtBg = Color.FromArgb(0xF0, 0xF0, 0xF0);
        private static readonly Color LtPanel = Color.White;
        private static readonly Color LtPanel2 = Color.White;
        private static readonly Color LtBorder = Color.FromArgb(0xD0, 0xD0, 0xD7);
        private static readonly Color LtText = Color.FromArgb(0x20, 0x20, 0x20);
        private static readonly Color LtSub = Color.FromArgb(0x60, 0x60, 0x60);
        private static readonly Color LtGreen = Color.FromArgb(0x2E, 0x7D, 0x32);
        private static readonly Color LtRed = Color.FromArgb(0xC6, 0x28, 0x28);
        private static readonly Color LtBlue = Color.FromArgb(0x15, 0x65, 0xC0);
        private static readonly Color LtPurple = Color.FromArgb(0x6A, 0x1B, 0x9A);
        private static readonly Color LtPurpleH = Color.FromArgb(0x4A, 0x14, 0x6E);
        private static readonly Color LtGrayBtn = Color.FromArgb(0xE2, 0xE2, 0xE6);
        private static readonly Color LtLogRecv = Color.FromArgb(0x1B, 0x5E, 0x20);
        private static readonly Color LtLogSend = Color.FromArgb(0x0D, 0x47, 0xA1);
        private static readonly Color LtLogSys = Color.FromArgb(0x60, 0x60, 0x60);
        private static readonly Color LtLogErr = Color.FromArgb(0xB7, 0x1C, 0x1C);

        // 当前主题下的工作色（由 ApplyTheme 填充）
        private Color ThBg, ThPanel, ThPanel2, ThBorder, ThText, ThSub, ThGreen, ThRed, ThBlue, ThPurple, ThPurpleH, ThGrayBtn;
        private Color ThLogRecv, ThLogSend, ThLogSys, ThLogErr, ThPlaceholder;

        // ===================== 构造函数 =====================
        public MainForm()
        {
            InitializeComponent();
            ApplyTheme();
            ApplyModeUI();
            ResetConnButtons();
            _loopTimer.Tick += LoopTimer_Tick;
        }

        // ===================== 界面布局（纯代码） =====================
        private void InitializeComponent()
        {
            this.Text = "TCP 调试助手";
            this.Size = new Size(920, 660);
            this.MinimumSize = new Size(700, 480);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("Microsoft YaHei", 9F);

            var tlp = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6
            };
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
            tlp.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
            this.Controls.Add(tlp);

            // ---------- 标题条 ----------
            pnlTitle = new Panel { Dock = DockStyle.Fill };
            lblTitle = new Label
            {
                Text = "TCP 调试助手",
                Font = new Font("Microsoft YaHei", 13F, FontStyle.Bold),
                AutoSize = true, Dock = DockStyle.Left,
                TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0)
            };
            var lblSub = new Label
            {
                Text = "TCP DEBUG CONSOLE",
                Font = new Font("Consolas", 9F), AutoSize = true, Dock = DockStyle.Right,
                TextAlign = ContentAlignment.MiddleRight, Margin = new Padding(0, 0, 12, 0)
            };
            pnlTitle.Controls.Add(lblTitle);
            pnlTitle.Controls.Add(lblSub);
            tlp.Controls.Add(pnlTitle, 0, 0);

            // ---------- 状态条 ----------
            pnlStatus = new Panel { Dock = DockStyle.Fill };
            var lblDot = new Label { Text = "●", Font = new Font("Consolas", 12F), AutoSize = true, Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(12, 0, 0, 0) };
            lblClientStatus = new Label { Text = "未连接", AutoSize = true, Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(4, 0, 0, 0) };
            chkTheme = new CheckBox { Text = "浅色", AutoSize = true, Dock = DockStyle.Right, Margin = new Padding(0, 7, 10, 0) };
            chkTheme.CheckedChanged += (s, e) => { _dark = !chkTheme.Checked; ApplyTheme(); };
            lblTxRx = new Label { Text = "TX: 0 B | RX: 0 B", AutoSize = true, Dock = DockStyle.Right, TextAlign = ContentAlignment.MiddleRight, Margin = new Padding(0, 0, 12, 0) };
            pnlStatus.Controls.Add(lblDot);
            pnlStatus.Controls.Add(lblClientStatus);
            pnlStatus.Controls.Add(chkTheme);
            pnlStatus.Controls.Add(lblTxRx);
            tlp.Controls.Add(pnlStatus, 0, 1);

            // ---------- 模式 + 连接设置 ----------
            pnlConn = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 8) };
            var flowMode = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 26, Padding = new Padding(0) };
            rdoServer = new RadioButton { Text = "服务器模式", Checked = true, AutoSize = true, Margin = new Padding(0, 3, 18, 0) };
            rdoClient = new RadioButton { Text = "客户端模式", AutoSize = true, Margin = new Padding(0, 3, 0, 0) };
            rdoServer.CheckedChanged += ModeRadio_CheckedChanged;
            rdoClient.CheckedChanged += ModeRadio_CheckedChanged;
            flowMode.Controls.Add(rdoServer); flowMode.Controls.Add(rdoClient);
            pnlConn.Controls.Add(flowMode);

            var flowSet = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 30, Margin = new Padding(0, 6, 0, 0), Padding = new Padding(0), WrapContents = true };
            lblLocalPort = new Label { Text = "本机端口：", AutoSize = true, Margin = new Padding(0, 6, 2, 0) };
            txtLocalPort = new TextBox { Text = "9000", Width = 80 };
            btnListen = new Button { Text = "开始监听", Width = 100, Height = 26 };
            btnListen.Click += BtnListen_Click;
            lblRemoteIp = new Label { Text = "目标 IP：", AutoSize = true, Margin = new Padding(0, 6, 2, 0) };
            txtRemoteIp = new TextBox { Text = "127.0.0.1", Width = 120 };
            lblRemotePort = new Label { Text = "端口：", AutoSize = true, Margin = new Padding(0, 6, 2, 0) };
            txtRemotePort = new TextBox { Text = "9000", Width = 70 };
            btnConnect = new Button { Text = "连接", Width = 100, Height = 26 };
            btnConnect.Click += BtnConnect_Click;
            flowSet.Controls.Add(lblLocalPort); flowSet.Controls.Add(txtLocalPort); flowSet.Controls.Add(btnListen);
            flowSet.Controls.Add(lblRemoteIp); flowSet.Controls.Add(txtRemoteIp); flowSet.Controls.Add(lblRemotePort); flowSet.Controls.Add(txtRemotePort); flowSet.Controls.Add(btnConnect);
            pnlConn.Controls.Add(flowSet);
            tlp.Controls.Add(pnlConn, 0, 2);

            // ---------- 连接列表 ----------
            pnlClients = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 6, 12, 6) };
            lblClients = new Label { Text = "连接列表", Dock = DockStyle.Top, Height = 18 };
            var flowClt = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 26, Padding = new Padding(0) };
            lblOnline = new Label { Text = "在线 0", AutoSize = true, Margin = new Padding(0, 4, 10, 0) };
            btnDisconnectSel = new Button { Text = "断开选中", Width = 90, Height = 24 };
            btnDisconnectSel.Click += BtnDisconnectSel_Click;
            flowClt.Controls.Add(lblOnline); flowClt.Controls.Add(btnDisconnectSel);
            lstClients = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
            lstClients.SelectedIndexChanged += LstClients_SelectedIndexChanged;
            pnlClients.Controls.Add(lblClients); pnlClients.Controls.Add(flowClt); pnlClients.Controls.Add(lstClients);
            tlp.Controls.Add(pnlClients, 0, 3);

            // ---------- 接收区 ----------
            pnlRecv = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 6, 12, 6) };
            var flowRecv = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 52, Padding = new Padding(0), WrapContents = true };
            chkHexView = new CheckBox { Text = "HEX 显示", AutoSize = true, Margin = new Padding(0, 4, 12, 4) };
            chkTimestamp = new CheckBox { Text = "显示时间戳", Checked = true, AutoSize = true, Margin = new Padding(0, 4, 12, 4) };
            chkAutoScroll = new CheckBox { Text = "自动滚屏", Checked = true, AutoSize = true, Margin = new Padding(0, 4, 12, 4) };
            chkTableView = new CheckBox { Text = "表格视图", AutoSize = true, Margin = new Padding(0, 4, 12, 4) };
            chkTableView.CheckedChanged += ChkTableView_CheckedChanged;
            btnSaveLog = new Button { Text = "保存日志", Width = 80, Height = 24, Margin = new Padding(0, 0, 8, 4) };
            btnSaveLog.Click += BtnSaveLog_Click;
            btnClearRecv = new Button { Text = "清空接收区", Width = 90, Height = 24, Margin = new Padding(0, 0, 0, 4) };
            btnClearRecv.Click += BtnClearRecv_Click;
            flowRecv.Controls.Add(chkHexView); flowRecv.Controls.Add(chkTimestamp); flowRecv.Controls.Add(chkAutoScroll);
            flowRecv.Controls.Add(chkTableView); flowRecv.Controls.Add(btnSaveLog); flowRecv.Controls.Add(btnClearRecv);
            txtLog = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9.5F), WordWrap = false };
            dgvRecv = new DataGridView
            {
                Dock = DockStyle.Fill, Visible = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                ReadOnly = true, RowHeadersVisible = false, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 9F)
            };
            dgvRecv.Columns.Add("Time", "时间");
            dgvRecv.Columns.Add("Dir", "方向");
            dgvRecv.Columns.Add("Len", "长度");
            dgvRecv.Columns.Add("Ascii", "ASCII");
            dgvRecv.Columns.Add("Hex", "HEX");
            dgvRecv.Columns["Time"].Width = 92;
            dgvRecv.Columns["Dir"].Width = 46;
            dgvRecv.Columns["Len"].Width = 50;
            dgvRecv.Columns["Ascii"].Width = 240;
            dgvRecv.Columns["Hex"].Width = 320;
            pnlRecv.Controls.Add(flowRecv);
            pnlRecv.Controls.Add(txtLog);
            pnlRecv.Controls.Add(dgvRecv);
            tlp.Controls.Add(pnlRecv, 0, 4);

            // ---------- 发送区 ----------
            pnlSend = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 6, 12, 6) };
            var flowSend = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 54, Padding = new Padding(0), WrapContents = true };
            chkSendHex = new CheckBox { Text = "HEX 发送", AutoSize = true, Margin = new Padding(0, 4, 10, 4) };
            chkAppendCRLF = new CheckBox { Text = "附加回车换行", AutoSize = true, Margin = new Padding(0, 4, 10, 4) };
            chkLoop = new CheckBox { Text = "循环发送", AutoSize = true, Margin = new Padding(0, 4, 4, 4) };
            chkLoop.CheckedChanged += ChkLoop_CheckedChanged;
            var lblInt = new Label { Text = "间隔(ms)：", AutoSize = true, Margin = new Padding(0, 6, 2, 4) };
            numLoopInterval = new NumericUpDown { Width = 70, Minimum = 100, Maximum = 60000, Value = 1000 };
            var lblCnt = new Label { Text = "次数(0=∞)：", AutoSize = true, Margin = new Padding(8, 6, 2, 4) };
            numLoopCount = new NumericUpDown { Width = 70, Minimum = 0, Maximum = 100000, Value = 0 };
            var lblEnc = new Label { Text = "编码：", AutoSize = true, Margin = new Padding(8, 6, 2, 4) };
            cmbEncoding = new ComboBox { Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbEncoding.Items.Add("ASCII"); cmbEncoding.Items.Add("UTF-8"); cmbEncoding.Items.Add("UTF-16"); cmbEncoding.Items.Add("GB2312(GBK)");
            cmbEncoding.SelectedIndex = 1;
            chkAutoReconnect = new CheckBox { Text = "断线重连", AutoSize = true, Margin = new Padding(8, 4, 6, 4) };
            chkAutoReconnect.CheckedChanged += (s, e) => { _autoReconnect = chkAutoReconnect.Checked; };
            btnSend = new Button { Text = "发送", Width = 100, Height = 26, Margin = new Padding(0, 0, 0, 4) };
            btnSend.Click += BtnSend_Click;
            flowSend.Controls.Add(chkSendHex); flowSend.Controls.Add(chkAppendCRLF); flowSend.Controls.Add(chkLoop);
            flowSend.Controls.Add(lblInt); flowSend.Controls.Add(numLoopInterval); flowSend.Controls.Add(lblCnt); flowSend.Controls.Add(numLoopCount);
            flowSend.Controls.Add(lblEnc); flowSend.Controls.Add(cmbEncoding); flowSend.Controls.Add(chkAutoReconnect); flowSend.Controls.Add(btnSend);
            txtSend = new RichTextBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 10F) };
            txtSend.Text = SendPlaceholder;
            txtSend.GotFocus += TxtSend_GotFocus;
            txtSend.LostFocus += TxtSend_LostFocus;
            txtSend.KeyDown += TxtSend_KeyDown;
            pnlSend.Controls.Add(flowSend);
            pnlSend.Controls.Add(txtSend);
            tlp.Controls.Add(pnlSend, 0, 5);

            this.FormClosing += MainForm_FormClosing;
        }

        // ===================== 主题应用 =====================
        private void ApplyTheme()
        {
            if (_dark)
            {
                ThBg = DkBg; ThPanel = DkPanel; ThPanel2 = DkPanel2; ThBorder = DkBorder; ThText = DkText; ThSub = DkSub;
                ThGreen = DkGreen; ThRed = DkRed; ThBlue = DkBlue; ThPurple = DkPurple; ThPurpleH = DkPurpleH; ThGrayBtn = DkGrayBtn;
                ThLogRecv = DkLogRecv; ThLogSend = DkLogSend; ThLogSys = DkLogSys; ThLogErr = DkLogErr;
                ThPlaceholder = Color.FromArgb(0x70, 0x70, 0x78);
            }
            else
            {
                ThBg = LtBg; ThPanel = LtPanel; ThPanel2 = LtPanel2; ThBorder = LtBorder; ThText = LtText; ThSub = LtSub;
                ThGreen = LtGreen; ThRed = LtRed; ThBlue = LtBlue; ThPurple = LtPurple; ThPurpleH = LtPurpleH; ThGrayBtn = LtGrayBtn;
                ThLogRecv = LtLogRecv; ThLogSend = LtLogSend; ThLogSys = LtLogSys; ThLogErr = LtLogErr;
                ThPlaceholder = Color.FromArgb(0xA0, 0xA0, 0xA8);
            }

            this.BackColor = ThBg;
            pnlTitle.BackColor = ThPanel; lblTitle.ForeColor = ThText;
            pnlStatus.BackColor = ThPanel2; lblClientStatus.ForeColor = ThText; lblTxRx.ForeColor = ThSub;
            pnlConn.BackColor = ThBg; pnlClients.BackColor = ThPanel; pnlRecv.BackColor = ThBg; pnlSend.BackColor = ThPanel;

            foreach (var tb in new[] { txtLocalPort, txtRemoteIp, txtRemotePort })
            { tb.BackColor = ThPanel2; tb.ForeColor = ThText; tb.BorderStyle = BorderStyle.FixedSingle; }
            numLoopInterval.BackColor = ThPanel2; numLoopInterval.ForeColor = ThText;
            numLoopCount.BackColor = ThPanel2; numLoopCount.ForeColor = ThText;
            cmbEncoding.BackColor = ThPanel2; cmbEncoding.ForeColor = ThText;
            txtLog.BackColor = ThPanel2; txtLog.ForeColor = ThText;
            txtSend.BackColor = ThPanel2;
            lstClients.BackColor = ThPanel2; lstClients.ForeColor = ThText;
            lblClients.ForeColor = ThSub; lblOnline.ForeColor = ThGreen;

            foreach (var c in new Control[] { rdoServer, rdoClient, chkHexView, chkTimestamp, chkAutoScroll,
                chkSendHex, chkAppendCRLF, chkLoop, chkTableView, chkTheme, chkAutoReconnect })
                c.ForeColor = ThText;

            // 表格视图配色
            dgvRecv.BackgroundColor = ThPanel2;
            dgvRecv.GridColor = ThBorder;
            dgvRecv.DefaultCellStyle.BackColor = ThPanel2;
            dgvRecv.DefaultCellStyle.ForeColor = ThText;
            dgvRecv.DefaultCellStyle.SelectionBackColor = ThBlue;
            dgvRecv.DefaultCellStyle.SelectionForeColor = Color.White;
            dgvRecv.ColumnHeadersDefaultCellStyle.BackColor = ThPanel;
            dgvRecv.ColumnHeadersDefaultCellStyle.ForeColor = ThText;
            dgvRecv.ColumnHeadersDefaultCellStyle.SelectionBackColor = ThPanel;
            dgvRecv.EnableHeadersVisualStyles = false;

            // 发送框占位提示色
            if (txtSend.Text == SendPlaceholder) txtSend.ForeColor = ThPlaceholder;

            // 按钮
            StyleButton(btnSend, ThPurple, Color.White);
            btnSend.FlatAppearance.MouseOverBackColor = ThPurpleH;
            StyleButton(btnSaveLog, ThGrayBtn, ThText);
            StyleButton(btnClearRecv, ThGrayBtn, ThText);
            StyleButton(btnDisconnectSel, ThGrayBtn, ThText);
            // 监听/连接按钮只设文字色（背景色由连接状态决定，不在此覆盖）
            btnListen.ForeColor = Color.White; btnConnect.ForeColor = Color.White;
            btnListen.FlatStyle = FlatStyle.Flat; btnConnect.FlatStyle = FlatStyle.Flat;
            btnListen.UseVisualStyleBackColor = false; btnConnect.UseVisualStyleBackColor = false;
        }

        private void ResetConnButtons()
        {
            StyleButton(btnListen, _dark ? DkGreen : LtGreen, Color.White);
            StyleButton(btnConnect, _dark ? DkBlue : LtBlue, Color.White);
        }

        private static void StyleButton(Button b, Color bg, Color fg)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = bg; b.ForeColor = fg;
            b.UseVisualStyleBackColor = false;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = ControlPaint.Dark(bg, 0.12f);
            b.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(bg, 0.22f);
        }

        // ===================== 模式切换 =====================
        private void ModeRadio_CheckedChanged(object sender, EventArgs e)
        {
            if (_switching) return;
            Mode newMode = rdoServer.Checked ? Mode.Server : Mode.Client;
            if (newMode == _mode) return;
            _switching = true;
            if (_listener != null) _ = StopServerAsync();
            if (_client != null) _ = DisconnectClientAsync();
            _mode = newMode;
            ApplyModeUI();
            _switching = false;
        }

        private void ApplyModeUI()
        {
            bool isServer = _mode == Mode.Server;
            lblLocalPort.Visible = txtLocalPort.Visible = btnListen.Visible = isServer;
            lblRemoteIp.Visible = txtRemoteIp.Visible = lblRemotePort.Visible = txtRemotePort.Visible = btnConnect.Visible = !isServer;
            lstClients.Visible = isServer;
            lblClients.Text = isServer ? "连接列表" : "（客户端模式，无需连接列表）";
        }

        // ===================== 服务端：监听 =====================
        private async void BtnListen_Click(object sender, EventArgs e)
        {
            if (_listener == null)
            {
                if (!int.TryParse(txtLocalPort.Text.Trim(), out int port) || port <= 0 || port > 65535)
                { MessageBox.Show("端口号无效（1-65535）。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                try
                {
                    _cts = new CancellationTokenSource();
                    _listener = new TcpListener(IPAddress.Any, port);
                    _listener.Start();
                    btnListen.Text = "停止监听";
                    StyleButton(btnListen, _dark ? DkRed : LtRed, Color.White);
                    SetClientStatus($"服务端监听中，端口 {port}");
                    Log($"[系统] 服务端已在端口 {port} 开始监听。\r\n", ThLogSys);
                    _ = AcceptLoop(_cts.Token);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("启动监听失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    _listener = null; _cts?.Dispose(); _cts = null;
                }
            }
            else
            {
                await StopServerAsync();
            }
        }

        private async Task AcceptLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(); }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    if (ct.IsCancellationRequested) break;
                    Log($"[系统] 接受连接异常：{ex.Message}\r\n", ThLogErr); break;
                }

                string peer = client.Client.RemoteEndPoint.ToString();
                lock (_clients)
                {
                    _clients[peer] = client;
                    if (!_clientKeys.Contains(peer)) _clientKeys.Add(peer);
                }
                AddClientToList(peer);
                SetClientStatus($"客户端接入： {peer}");
                Log($"[系统] 客户端已连接：{peer}\r\n", ThLogSys);
                var stream = client.GetStream();
                _ = ReceiveLoop(stream, peer, ct);
            }
        }

        private async Task StopServerAsync()
        {
            try { _cts?.Cancel(); } catch { }
            lock (_clients)
            {
                foreach (var c in _clients.Values) { try { c.Close(); } catch { } }
                _clients.Clear(); _clientKeys.Clear();
            }
            ClearClientList();
            if (_listener != null) { try { _listener.Stop(); } catch { } _listener = null; }
            await Task.Delay(100);
            _cts?.Dispose(); _cts = null;
            btnListen.Text = "开始监听";
            StyleButton(btnListen, _dark ? DkGreen : LtGreen, Color.White);
            SetClientStatus("已停止监听");
            UpdateOnline();
            Log("[系统] 服务端已停止。\r\n", ThLogSys);
        }

        // ===================== 客户端：连接 =====================
        private async void BtnConnect_Click(object sender, EventArgs e)
        {
            if (_client == null) await ConnectAsync();
            else await DisconnectClientAsync(manual: true);
        }

        private async Task ConnectAsync()
        {
            string host = txtRemoteIp.Text.Trim();
            if (!int.TryParse(txtRemotePort.Text.Trim(), out int port) || port <= 0 || port > 65535)
            { MessageBox.Show("端口号无效（1-65535）。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try
            {
                _cts = new CancellationTokenSource();
                _client = new TcpClient();
                SetClientStatus($"正在连接 {host}:{port} ...");
                await _client.ConnectAsync(host, port);
                _clientStream = _client.GetStream();
                btnConnect.Text = "断开";
                StyleButton(btnConnect, _dark ? DkRed : LtRed, Color.White);
                SetClientStatus($"已连接到 {host}:{port}");
                Log($"[系统] 已连接到 {host}:{port}。\r\n", ThLogSys);
                _ = ReceiveLoop(_clientStream, $"{host}:{port}", _cts.Token);
            }
            catch (Exception ex)
            {
                MessageBox.Show("连接失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _client?.Close(); _client = null; _cts?.Dispose(); _cts = null;
                SetClientStatus("未连接");
            }
        }

        private async Task DisconnectClientAsync(bool manual = false)
        {
            try { _cts?.Cancel(); } catch { }
            try { _client?.Close(); } catch { }
            _client = null; _clientStream = null;
            await Task.Delay(100);
            _cts?.Dispose(); _cts = null;
            btnConnect.Text = "连接";
            StyleButton(btnConnect, _dark ? DkBlue : LtBlue, Color.White);
            SetClientStatus("未连接");
            Log("[系统] 已断开连接。\r\n", ThLogSys);
            if (manual) { _autoReconnect = false; chkAutoReconnect.Checked = false; }
        }

        // ===================== 接收循环 =====================
        private async Task ReceiveLoop(NetworkStream stream, string peer, CancellationToken ct)
        {
            byte[] buf = new byte[4096];
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    int n;
                    try { n = await stream.ReadAsync(buf, 0, buf.Length, ct); }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex) { Log($"[系统] {peer} 接收异常：{ex.Message}\r\n", ThLogErr); break; }

                    if (n == 0)
                    {
                        Log($"[系统] {peer} 断开连接。\r\n", ThLogSys);
                        if (_mode == Mode.Server) RemoveClient(peer);
                        else
                        {
                            if (_autoReconnect) { SetClientStatus("连接断开，正在重连…"); _ = TryReconnectAsync(); }
                            else await DisconnectClientAsync();
                        }
                        break;
                    }

                    byte[] data = new byte[n];
                    Array.Copy(buf, data, n);
                    Interlocked.Add(ref _recvBytes, n);
                    Interlocked.Increment(ref _recvPackets);
                    UpdateStats();
                    RecordTraffic("收", peer, data);
                }
            }
            finally
            {
                if (_mode == Mode.Server) RemoveClient(peer);
            }
        }

        // ===================== 发送 =====================
        private async void BtnSend_Click(object sender, EventArgs e) => await DoSendAsync();

        private async Task DoSendAsync()
        {
            string raw = (txtSend.Text == SendPlaceholder) ? "" : txtSend.Text;
            if (string.IsNullOrEmpty(raw)) return;

            byte[] bytes;
            try
            {
                if (chkSendHex.Checked) bytes = ParseHex(raw);
                else
                {
                    bytes = GetEncoding().GetBytes(raw);
                    if (chkAppendCRLF.Checked)
                    {
                        var list = new List<byte>(bytes);
                        list.AddRange(new byte[] { 0x0D, 0x0A });
                        bytes = list.ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("发送内容解析失败：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (bytes.Length == 0) return;

            try
            {
                if (_mode == Mode.Server)
                {
                    if (_listener == null) { MessageBox.Show("请先开始监听。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    string selected = lstClients.SelectedItem as string;
                    var targets = new List<KeyValuePair<string, TcpClient>>();
                    lock (_clients)
                    {
                        foreach (var kvp in _clients)
                            if (selected == null || kvp.Key == selected) targets.Add(kvp);
                    }
                    int sent = 0;
                    foreach (var kvp in targets)
                    {
                        try { await kvp.Value.GetStream().WriteAsync(bytes, 0, bytes.Length); sent++; }
                        catch (Exception ex) { Log($"[系统] 发送给 {kvp.Key} 失败：{ex.Message}\r\n", ThLogErr); }
                    }
                    if (sent == 0) { MessageBox.Show("没有可用的客户端连接。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    RecordTraffic("发", selected ?? "广播", bytes);
                }
                else
                {
                    if (_clientStream == null) { MessageBox.Show("请先连接服务器。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    await _clientStream.WriteAsync(bytes, 0, bytes.Length);
                    string peer = txtRemoteIp.Text.Trim() + ":" + txtRemotePort.Text.Trim();
                    RecordTraffic("发", peer, bytes);
                }

                Interlocked.Add(ref _sendBytes, bytes.Length);
                Interlocked.Increment(ref _sendPackets);
                UpdateStats();
                // 发送后清空输入框，方便下次输入
                txtSend.Text = "";
                txtSend.ForeColor = ThText;
                txtSend.Focus();
            }
            catch (Exception ex) { Log($"[系统] 发送失败：{ex.Message}\r\n", ThLogErr); }
        }

        // 同时写入文本日志与表格视图
        private void RecordTraffic(string dir, string peer, byte[] data)
        {
            string ascii = chkHexView.Checked ? BytesToHexLines(data) : GetEncoding().GetString(data);
            string head = chkTimestamp.Checked ? $"[{DateTime.Now:HH:mm:ss.fff}] " : "";
            if (dir == "收")
                Log(head + $"[收← {peer} ({data.Length} 字节)] " + ascii + "\r\n", ThLogRecv);
            else
                Log(head + $"[发→ {peer} ({data.Length} 字节)] " + ascii + "\r\n", ThLogSend);
            AddGridRow(dir, peer, data);
        }

        private void AddGridRow(string dir, string peer, byte[] data)
        {
            if (dgvRecv.InvokeRequired) { dgvRecv.Invoke(new Action<string, string, byte[]>(AddGridRow), dir, peer, data); return; }
            string ascii = ToPrintable(data);
            string hex = BitConverter.ToString(data).Replace("-", " ");
            if (hex.Length > 240) hex = hex.Substring(0, 240) + " …";
            dgvRecv.Rows.Add(DateTime.Now.ToString("HH:mm:ss.fff"), dir, data.Length, ascii, hex);
            if (chkAutoScroll.Checked && dgvRecv.Rows.Count > 0)
                dgvRecv.FirstDisplayedScrollingRowIndex = dgvRecv.Rows.Count - 1;
        }

        private static string ToPrintable(byte[] data)
        {
            var sb = new StringBuilder();
            foreach (byte b in data)
                sb.Append((b >= 0x20 && b < 0x7F) ? (char)b : '.');
            return sb.ToString();
        }

        // ===================== 客户端列表维护 =====================
        private void AddClientToList(string peer)
        {
            if (lstClients.InvokeRequired) { lstClients.Invoke(new Action<string>(AddClientToList), peer); return; }
            lstClients.Items.Add(peer); UpdateOnline();
        }
        private void ClearClientList()
        {
            if (lstClients.InvokeRequired) { lstClients.Invoke(new Action(ClearClientList)); return; }
            lstClients.Items.Clear(); UpdateOnline();
        }
        private void RemoveClient(string peer)
        {
            TcpClient removed;
            lock (_clients)
            {
                _clients.TryGetValue(peer, out removed);
                _clients.Remove(peer); _clientKeys.Remove(peer);
            }
            try { removed?.Close(); } catch { }
            if (lstClients.InvokeRequired) { lstClients.Invoke(new Action<string>(RemoveClientItem), peer); }
            else RemoveClientItem(peer);
            UpdateOnline();
        }
        private void RemoveClientItem(string peer)
        {
            int idx = lstClients.Items.IndexOf(peer);
            if (idx >= 0) lstClients.Items.RemoveAt(idx);
        }
        private void UpdateOnline()
        {
            if (lblOnline.InvokeRequired) { lblOnline.Invoke(new Action(UpdateOnline)); return; }
            lblOnline.Text = "在线 " + lstClients.Items.Count;
        }
        private void LstClients_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lstClients.SelectedItem == null)
                SetClientStatus("服务端监听中（未选择客户端 → 发送将广播）");
            else
                SetClientStatus($"服务端监听中（已选择 {lstClients.SelectedItem} → 仅发该客户端）");
        }

        private void BtnDisconnectSel_Click(object sender, EventArgs e)
        {
            var sel = lstClients.SelectedItem as string;
            if (sel == null) { MessageBox.Show("请先在列表中选择要断开的客户端。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            RemoveClient(sel);
            Log($"[系统] 已手动断开：{sel}\r\n", ThLogSys);
        }

        // ===================== 循环发送 =====================
        private void ChkLoop_CheckedChanged(object sender, EventArgs e)
        {
            if (chkLoop.Checked)
            {
                bool ready = (_mode == Mode.Server && _listener != null) || (_mode == Mode.Client && _clientStream != null);
                if (!ready)
                {
                    MessageBox.Show("请先建立连接/开始监听，再启用循环发送。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    chkLoop.Checked = false; return;
                }
                _loopSent = 0;
                _loopTimer.Interval = (int)numLoopInterval.Value;
                _loopTimer.Start();
                Log($"[系统] 已开启循环发送，间隔 {numLoopInterval.Value} 毫秒，次数 {(numLoopCount.Value > 0 ? numLoopCount.Value.ToString() : "无限")}。\r\n", ThLogSys);
            }
            else
            {
                _loopTimer.Stop();
                Log("[系统] 已关闭循环发送。\r\n", ThLogSys);
            }
        }
        private async void LoopTimer_Tick(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtSend.Text) || txtSend.Text == SendPlaceholder) return;
            await DoSendAsync();
            _loopSent++;
            if (numLoopCount.Value > 0 && _loopSent >= (int)numLoopCount.Value)
            {
                chkLoop.Checked = false;
                _loopTimer.Stop();
                Log($"[系统] 循环发送已达 {numLoopCount.Value} 次，已停止。\r\n", ThLogSys);
            }
        }

        // ===================== 客户端断线自动重连 =====================
        private async Task TryReconnectAsync()
        {
            await Task.Delay(3000);
            if (_mode != Mode.Client || _client != null || !_autoReconnect) return;
            await ConnectAsync();
            if (_client == null && _autoReconnect) _ = TryReconnectAsync(); // 仍失败则继续重试
        }

        // ===================== 发送框：占位提示 + Enter 发送 =====================
        private void TxtSend_GotFocus(object sender, EventArgs e)
        {
            if (txtSend.Text == SendPlaceholder) { txtSend.Text = ""; txtSend.ForeColor = ThText; }
        }
        private void TxtSend_LostFocus(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(txtSend.Text)) { txtSend.Text = SendPlaceholder; txtSend.ForeColor = ThPlaceholder; }
        }
        private void TxtSend_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                _ = DoSendAsync();
            }
        }

        // ===================== 接收区：表格视图切换 / 保存 / 清空 =====================
        private void ChkTableView_CheckedChanged(object sender, EventArgs e)
        {
            bool table = chkTableView.Checked;
            txtLog.Visible = !table;
            dgvRecv.Visible = table;
        }
        private void BtnSaveLog_Click(object sender, EventArgs e)
        {
            using (var sfd = new SaveFileDialog { Filter = "文本文件|*.txt|CSV 文件|*.csv|所有文件|*.*", FileName = "tcp_log.txt" })
            {
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        if (sfd.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) && dgvRecv.Rows.Count > 0)
                        {
                            var sb = new StringBuilder();
                            foreach (DataGridViewRow r in dgvRecv.Rows)
                                sb.AppendLine(string.Join(",", r.Cells[0].Value, r.Cells[1].Value, r.Cells[2].Value, "\"" + r.Cells[3].Value + "\"", "\"" + r.Cells[4].Value + "\""));
                            File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
                        }
                        else
                        {
                            File.WriteAllText(sfd.FileName, txtLog.Text, Encoding.UTF8);
                        }
                        Log("[系统] 日志已保存。\r\n", ThLogSys);
                    }
                    catch (Exception ex) { MessageBox.Show("保存失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }
            }
        }
        private void BtnClearRecv_Click(object sender, EventArgs e)
        {
            txtLog.Clear();
            if (dgvRecv.Rows.Count > 0) dgvRecv.Rows.Clear();
        }

        // ===================== 编码 / HEX 工具 =====================
        private Encoding GetEncoding()
        {
            switch (cmbEncoding.SelectedIndex)
            {
                case 0: return Encoding.ASCII;
                case 1: return Encoding.UTF8;
                case 2: return Encoding.Unicode;
                case 3: return Encoding.GetEncoding("GB2312");
                default: return Encoding.UTF8;
            }
        }

        private static byte[] ParseHex(string text)
        {
            string hex = text.Replace(" ", "").Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace("-", "");
            if (hex.Length % 2 != 0) throw new FormatException("HEX 字符串长度必须为偶数。");
            if (hex.Length == 0) return new byte[0];
            byte[] result = new byte[hex.Length / 2];
            for (int i = 0; i < result.Length; i++)
                result[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return result;
        }

        private static string BytesToHexLines(byte[] data)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < data.Length; i += 16)
            {
                int len = Math.Min(16, data.Length - i);
                var line = new StringBuilder();
                for (int j = 0; j < len; j++) { line.Append(data[i + j].ToString("X2")); line.Append(' '); }
                sb.AppendLine(line.ToString());
            }
            return sb.ToString().TrimEnd();
        }

        // ===================== 日志 / 状态 / 统计 =====================
        private void Log(string text, Color color)
        {
            if (txtLog.InvokeRequired) { txtLog.Invoke(new Action<string, Color>(Log), text, color); return; }
            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.SelectionColor = color;
            txtLog.AppendText(text);
            txtLog.SelectionColor = txtLog.ForeColor;
            if (chkAutoScroll.Checked)
            {
                txtLog.SelectionStart = txtLog.TextLength;
                txtLog.ScrollToCaret();
            }
        }

        private void SetClientStatus(string text)
        {
            if (lblClientStatus.InvokeRequired) { lblClientStatus.Invoke(new Action<string>(SetClientStatus), text); return; }
            lblClientStatus.Text = text;
        }

        private void UpdateStats()
        {
            if (lblTxRx.InvokeRequired) { lblTxRx.Invoke(new Action(UpdateStats)); return; }
            lblTxRx.Text = $"TX: {_sendBytes} B ({_sendPackets}包) | RX: {_recvBytes} B ({_recvPackets}包)";
        }

        // ===================== 关闭窗体：释放资源 =====================
        private async void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            try { _cts?.Cancel(); } catch { }
            if (_listener != null) { try { _listener.Stop(); } catch { } }
            if (_client != null) { try { _client.Close(); } catch { } }
            lock (_clients) { foreach (var c in _clients.Values) { try { c.Close(); } catch { } } }
            _loopTimer.Stop();
            await Task.Delay(50);
        }
    }
}
