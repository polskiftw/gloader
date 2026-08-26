namespace Terraria
{
    public class Main
    {
    }

    public class MessageBuffer
    {
        public byte[] readBuffer = new byte[256];
        public int whoAmI;

        public void GetData(int start, int length, out int messageType)
        {
            messageType = 0;
        }
    }
}

namespace Terraria.Localization
{
    public class NetworkText
    {
    }
}
