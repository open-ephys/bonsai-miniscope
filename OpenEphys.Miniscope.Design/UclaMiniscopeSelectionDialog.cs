using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Bonsai.Reactive;
using OpenCV.Net;

namespace OpenEphys.Miniscope.Design
{
    public enum ScopeKind
    {
        V3,
        V4,
        MiniCam
    }
    public partial class UclaMiniscopeSelectionDialog : Form
    {
        bool scanning = false;
        CancellationTokenSource tokenSource = new();
        Task cancellableTask;
        readonly ScopeKind scopeKind;

        public UclaMiniscopeSelectionDialog(ScopeKind kind)
        {
            InitializeComponent();
            scopeKind = kind;
        }

        void PerformScan(CancellationToken ct, int maxIterations)
        {
            // Was cancellation already requested?
            if (ct.IsCancellationRequested)
            {
                return;
            }

            for (int i = 0; i < maxIterations; i++)
            {
                using (var capture = OpenCV.Net.Capture.CreateCameraCapture(i, CaptureDomain.DirectShow))
                {
                    var originalState = scopeKind switch
                    {
                        ScopeKind.V3 => UclaMiniscopeV3.IssueStartCommands(capture),
                        ScopeKind.V4 => UclaMiniscopeV4.IssueStartCommands(capture),
                        ScopeKind.MiniCam => UclaMiniCam.IssueStartCommands(capture),
                        _ => throw new NotImplementedException(),
                    };

                    if (capture.QueryFrame() != null)
                    {
                        //cameras.Add((i, (int)capture.GetProperty(CaptureProperty.Sharpness)));
                        var fc = (int)capture.GetProperty(CaptureProperty.Contrast);
                        Thread.Sleep(200);

                        if ((int)capture.GetProperty(CaptureProperty.Contrast) > fc)
                        {
                            listBox_Indices.Invoke((MethodInvoker)delegate
                            {
                                listBox_Indices.Items.Add(i);
                                listBox_Indices.Update();

                            });
                        }
                    }

                    switch(scopeKind)
                    {
                        case ScopeKind.V3:
                            UclaMiniscopeV3.IssueStopCommands(capture, originalState);
                            break;
                        case ScopeKind.V4:
                            UclaMiniscopeV4.IssueStopCommands(capture, originalState);
                            break;
                        case ScopeKind.MiniCam:
                            UclaMiniCam.IssueStopCommands(capture, originalState);
                            break;
                    };

                    capture.Close();
                }

                if (ct.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        void StartScan()
        {
            buttonScan.Text = "Stop Scan";
            toolStripStatusLabel.Text = "Scanning...";
            listBox_Indices.Items.Clear();
            scanning = true;
        }

        async void CancelScan()
        {
            if (cancellableTask != null)
            {
                if (!cancellableTask.IsCanceled && !tokenSource.IsCancellationRequested)
                {
                    tokenSource.Cancel();
                    cancellableTask.Wait();
                }
            }
        }

        void FinishScanInvoke()
        {
            if (!tokenSource.IsCancellationRequested)
            {
                buttonScan.Invoke((MethodInvoker)delegate
                {
                    buttonScan.Text = "Scan";
                });

                toolStripStatusLabel.Text = "Idle";
                scanning = false;
            }
        }

        private void buttonScan_Click(object sender, EventArgs e)
        {
            if (!scanning)
            {
                StartScan();
                cancellableTask = Task.Factory.StartNew(() => {
                    PerformScan(tokenSource.Token, 100);
                    FinishScanInvoke();
                }, tokenSource.Token);
            }
            else
            {
                toolStripStatusLabel.Text = "Stopping...";
                CancelScan();
                buttonScan.Text = "Scan";
                toolStripStatusLabel.Text = "Idle";
                scanning = false;
            }
        }

        private void buttonOK_Click(object sender, EventArgs e)
        {
            CancelScan();
            tokenSource.Dispose();
            DialogResult = DialogResult.OK;
            Close();
        }

        private async void buttonCancel_Click(object sender, EventArgs e)
        {
            CancelScan();
            tokenSource.Dispose();
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
