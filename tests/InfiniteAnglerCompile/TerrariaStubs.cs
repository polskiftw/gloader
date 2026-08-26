namespace Microsoft.Xna.Framework
{
    public struct Color
    {
        public static Color Yellow => new Color();
    }
}

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

namespace Terraria.Chat
{
    public sealed class ChatMessage
    {
        public string Text { get; set; }
        public bool IsConsumed { get; private set; }

        public ChatMessage(string text)
        {
            Text = text;
        }

        public void Consume()
        {
            IsConsumed = true;
        }
    }

    public class ChatCommandProcessor
    {
        public void ProcessIncomingMessage(ChatMessage message, int clientId)
        {
        }
    }

    public static class ChatHelper
    {
        public static void SendChatMessageToClient(
            Terraria.Localization.NetworkText text,
            Microsoft.Xna.Framework.Color color,
            int playerId)
        {
        }
    }
}

namespace Terraria.Localization
{
    public class NetworkText
    {
        public static NetworkText FromLiteral(string text)
        {
            return new NetworkText();
        }
    }
}
