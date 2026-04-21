namespace OpenEphys.Miniscope
{
    internal interface IMiniscopeDaqControls
    {
        II2COverUVC I2C { get; }

        void EnableFrameTTLs(bool enabled);
    }

    internal interface II2COverUVC
    {
        void QueueCommand(byte address, params byte[] data);
        void CommitCommands();
    }
}
