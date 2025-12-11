using System;
using System.IO;
using System.Text;

namespace uchat_gui
{
    public enum FrameType : byte
    {
        Text = 1,
        FileChunk = 2
    }

    public class Frame
    {
        public FrameType Type { get; }
        public byte[] Payload { get; }

        public Frame(FrameType type, byte[] payload)
        {
            Type = type;
            Payload = payload ?? Array.Empty<byte>();
        }
    }

    public static class FrameIO
    {
        public static Frame? ReadFrame(BinaryReader reader)
        {
            try
            {
                byte type = reader.ReadByte();
                int len = reader.ReadInt32();

                if (len < 0)
                    throw new IOException("Invalid frame length");

                byte[] data = reader.ReadBytes(len);
                if (data.Length < len)
                    throw new IOException("Unexpected stream end");

                return new Frame((FrameType)type, data);
            }
            catch (EndOfStreamException)
            {
                return null;
            }
        }

        public static void WriteFrame(BinaryWriter writer, Frame frame)
        {
            writer.Write((byte)frame.Type);
            writer.Write(frame.Payload.Length);
            writer.Write(frame.Payload);
            writer.Flush();
        }

        public static void SendText(BinaryWriter writer, string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            WriteFrame(writer, new Frame(FrameType.Text, data));
        }
    }
}

