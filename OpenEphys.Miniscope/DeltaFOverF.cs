using Bonsai;
using System;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Linq;
using OpenCV.Net;
using System.Collections.Generic;

namespace OpenEphys.Miniscope
{
    [Description("Performs a naïve, causal deltaF/F calculation on a series of images. In general, regardless of how its computed, deltaF/F should not be used for quantitative analysis due to to its dependence on accurate background estimation and the (generally) nonlinear relationship between fluorescence intensity and the underlying independent variable of interest (e.g. Calcium levels).")]
    public class DeltaFOverF : Transform<IplImage, (IplImage, IplImage)>
    {
        [Range(2, 1000)]
        [Editor(DesignTypes.NumericUpDownEditor, DesignTypes.UITypeEditor)]
        [Description("The number of previous frames to average to determine the background fluorescence.")]
        public int BackgroundFrames { get; set; } = 100;

        [Range(0, 255)]
        [Precision(1, 0.1)]
        [Editor(DesignTypes.SliderEditor, DesignTypes.UITypeEditor)]
        [Description("The minimum background intensity level required to calculate dFF. This prevents division by a small number which results in pixels that dominate the image.")]
        public double BackgroundThreshold { get; set; } = 20;

        [Editor(DesignTypes.NumericUpDownEditor, DesignTypes.UITypeEditor)]
        [Description("The standard deviation, in pixels, of a Gaussian function that approximates the spatial extent of a typical region of interest (e.g. a cell body). If set to 0, data calculation is performed without spatial smoothing.")]
        public int Sigma { get; set; } = 2;

        public override IObservable<(IplImage, IplImage)> Process(IObservable<IplImage> source)
        {
            return Observable.Defer(() =>
            {
                IplImage greyImage = null;
                IplImage dummy = null;
                IplImage floatImage = null;
                IplImage background = null;
                Queue<IplImage> backgroundBuffer = null;

                bool convertToGrey = false;
                var backgroundFrames = BackgroundFrames;

                return source.Select(input =>
                {
                    if (greyImage == null || greyImage.Size != input.Size)
                    {
                        convertToGrey = input.Channels > 1;
                        backgroundBuffer = new Queue<IplImage>(backgroundFrames);

                        for (int i = 0; i < backgroundFrames; i++)
                            backgroundBuffer.Enqueue(new IplImage(input.Size, IplDepth.F32, 1));

                        greyImage = new IplImage(input.Size, input.Depth, 1);
                        dummy = new IplImage(input.Size, input.Depth, 1);
                        floatImage = new IplImage(input.Size, IplDepth.F32, 1);
                        background = new IplImage(input.Size, IplDepth.F32, 1);
                    }

                    // Convert to floating point type
                    if (convertToGrey)
                    {
                        CV.CvtColor(input, greyImage, ColorConversion.Bgr2Gray);
                        CV.Convert(greyImage, floatImage);
                    } else
                    {
                        CV.Convert(input, floatImage);
                    }

                    var sigma = Sigma;
                    if (sigma > 0)
                        CV.Smooth(floatImage, floatImage, SmoothMethod.Gaussian, 2 * sigma + 1, 0, sigma);

                    background -= backgroundBuffer.Dequeue();
                    background += floatImage / backgroundFrames;
                    backgroundBuffer.Enqueue(floatImage / backgroundFrames);

                    var dff = (floatImage - background) / background;

                    var maskBackground = new IplImage(dff.Size, IplDepth.U8, 1);
                    CV.Threshold(background, maskBackground, BackgroundThreshold, 1, ThresholdTypes.Binary);

                    var maskDff = new IplImage(dff.Size, IplDepth.U8, 1);
                    CV.Threshold(dff, maskDff, 0, 1, ThresholdTypes.Binary);

                    var dffMasked = new IplImage(dff.Size, dff.Depth, dff.Channels);
                    CV.Copy(dff, dffMasked, maskBackground * maskDff);

                    return (background, dffMasked);
                });
            });
        }
    }
}
