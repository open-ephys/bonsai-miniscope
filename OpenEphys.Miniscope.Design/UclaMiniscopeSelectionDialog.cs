using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenCV.Net;

namespace OpenEphys.Miniscope.Design
{
    /// <summary>
    /// Specifies the kind of UCLA Miniscope device to scan for.
    /// </summary>
    public enum ScopeKind
    {
        /// <summary>UCLA Miniscope V3.</summary>
        V3,
        /// <summary>UCLA Miniscope V4.</summary>
        V4,
        /// <summary>UCLA MiniCAM behavioral monitoring camera.</summary>
        MiniCam
    }

    /// <summary>
    /// A dialog that scans for connected UCLA Miniscope devices and allows the user to select one by index.
    /// </summary>
    public partial class UclaMiniscopeSelectionDialog : Form
    {
        bool scanning = false;
        CancellationTokenSource tokenSource = new();
        Task cancellableTask;
        readonly ScopeKind scopeKind;

        /// <summary>
        /// Initializes a new instance of the <see cref="UclaMiniscopeSelectionDialog"/> class.
        /// </summary>
        /// <param name="kind">The kind of Miniscope device to scan for.</param>
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

                    switch (scopeKind)
                    {
                        case ScopeKind.V3:
                            UclaMiniscopeV3.IssueStopCommands(capture, originalState);
                            break;
                        case ScopeKind.MiniCam:
                            UclaMiniCam.IssueStopCommands(capture, originalState);
                            break;
                    }
                    ;

                    capture.Close();
                }

                if (ct.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        void CancelScan()
        {
            toolStripStatusLabel.Text = "Stopping...";

            if (cancellableTask != null)
            {
                if (!cancellableTask.IsCanceled && !tokenSource.IsCancellationRequested)
                {
                    tokenSource.Cancel();
                    cancellableTask.Wait(1000);
                }

            }

            scanning = false;
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
                buttonScan.Text = "Stop Scan";
                toolStripStatusLabel.Text = "Scanning...";
                listBox_Indices.Items.Clear();
                scanning = true;

                if (tokenSource.IsCancellationRequested)
                {
                    tokenSource.Dispose();
                    tokenSource = new CancellationTokenSource();
                }
                cancellableTask = Task.Factory.StartNew(() => {
                    PerformScan(tokenSource.Token, 100);
                    FinishScanInvoke();
                }, tokenSource.Token);
            }
            else
            {
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
