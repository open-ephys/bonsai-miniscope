namespace OpenEphys.Miniscope
{
    internal struct AbusedUvcRegisters
    {
        public AbusedUvcRegisters(double contrast, double gamma, double sharpness, double saturation)
        {
            Contrast = contrast;
            Gamma = gamma;
            Sharpness = sharpness;
            Saturation = saturation;
        }

        public double Contrast { get; }
        public double Gamma { get; }
        public double Sharpness { get; }
        public double Saturation { get; }

    }
}
