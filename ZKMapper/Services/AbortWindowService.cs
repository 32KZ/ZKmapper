using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace ZKMapper.Services;

internal sealed class AbortWindowService : IDisposable
{
    private readonly Action _onAbortRequested;
    private readonly Thread _uiThread;
    private readonly TaskCompletionSource<AbortWindowForm> _formReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _abortRequested;

    private AbortWindowService(Action onAbortRequested)
    {
        _onAbortRequested = onAbortRequested;
        _uiThread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "ZKMapper Abort Window"
        };
        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Start();
    }

    public static AbortWindowService Start(Action onAbortRequested)
    {
        return new AbortWindowService(onAbortRequested);
    }

    public void Dispose()
    {
        if (!_formReady.Task.Wait(TimeSpan.FromSeconds(5)))
        {
            return;
        }

        var form = _formReady.Task.Result;
        try
        {
            if (form.IsDisposed)
            {
                return;
            }

            if (form.InvokeRequired)
            {
                form.BeginInvoke(new Action(form.Close));
            }
            else
            {
                form.Close();
            }
        }
        catch (ObjectDisposedException)
        {
        }

        _uiThread.Join(TimeSpan.FromSeconds(5));
    }

    private void RunMessageLoop()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var form = new AbortWindowForm(RequestAbort);
        form.FormClosed += (_, _) => Application.ExitThread();
        _formReady.SetResult(form);
        Application.Run(form);
    }

    private void RequestAbort()
    {
        if (Interlocked.Exchange(ref _abortRequested, 1) == 1)
        {
            return;
        }

        _onAbortRequested();
    }

    private sealed class AbortWindowForm : Form
    {
        private readonly Action _requestAbort;
        private readonly Button _abortButton;
        private readonly Label _statusLabel;

        public AbortWindowForm(Action requestAbort)
        {
            _requestAbort = requestAbort;
            Text = "ZKMapper Abort";
            StartPosition = FormStartPosition.Manual;
            Location = new Point(40, 40);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = false;
            TopMost = true;
            ClientSize = new Size(260, 120);
            BackColor = Color.FromArgb(28, 28, 28);

            _statusLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 48,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                Text = "Click abort to stop after the current profile."
            };

            _abortButton = new Button
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                Text = "ABORT",
                BackColor = Color.FromArgb(192, 32, 32),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 12, FontStyle.Bold)
            };
            _abortButton.FlatAppearance.BorderSize = 0;
            _abortButton.Click += (_, _) => OnAbortClicked();

            Controls.Add(_statusLabel);
            Controls.Add(_abortButton);
        }

        private void OnAbortClicked()
        {
            _abortButton.Enabled = false;
            _abortButton.Text = "ABORT REQUESTED";
            _statusLabel.Text = "Stopping mapping and returning to the menu...";
            _requestAbort();
        }
    }
}
