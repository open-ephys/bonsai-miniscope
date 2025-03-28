using Bonsai;
using System;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Linq;
using OpenCV.Net;

namespace OpenEphys.Miniscope
{
    [Description("Performs a naïve, real-time deltaF/F calculation on a series of images.")]
    public class DeltaFOverF : Transform<IplImage, IplImage>
    {
        [Range(0, 1)]
        [Precision(2, .01)]
        [Editor(DesignTypes.SliderEditor, DesignTypes.UITypeEditor)]
        [Description("Determines how fast the online estimation of the background is adapted. " +
            "Pick a value such that 1 / AdaptationRate, in units of frames, is significantly longer than the time constant of your indicator.")]
        public double AdaptationRate { get; set; } = 0.01;


        [Editor(DesignTypes.NumericUpDownEditor, DesignTypes.UITypeEditor)]
        [Description("The standard deviation, in pixels, of a Gaussian function that approximates the spatial extent of a typical region of interest (e.g. a cell body). This is used to calculate the background fluorescence.")]
        public int Sigma { get; set; } = 3;

        public override IObservable<IplImage> Process(IObservable<IplImage> source)
        {
            return Observable.Defer(() =>
            {
                IplImage image = null;
                IplImage difference = null;
                IplImage background = null;
                return source.Select(input =>
                {
                    if (AdaptationRate <= 0)
                    {
                        throw new InvalidOperationException($"{nameof(AdaptationRate)} must be greater than 0");
                    }

                    if (background == null || background.Size != input.Size)
                    {
                        image = new IplImage(input.Size, IplDepth.F32, input.Channels);
                        difference = new IplImage(input.Size, IplDepth.F32, input.Channels);
                        background = new IplImage(input.Size, IplDepth.F32, input.Channels);
                        background.SetZero();
                    }

                    var sigma = Sigma % 2 == 0 ? Sigma + 1 : Sigma;

                    CV.Convert(input, image); // Convert to floating point type
                    CV.Smooth(image, image, SmoothMethod.Gaussian, 2*sigma + 1, 0, sigma);
                    CV.RunningAvg(image, background, AdaptationRate);
                    CV.Sub(image, background, difference);
                    return difference / background;
                });
            });
        }
    }
}
