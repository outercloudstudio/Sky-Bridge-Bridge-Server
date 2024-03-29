﻿using System.Text;

namespace SkyBridge
{
    [Serializable]
    public class Packet
    {
        [Serializable]
        public class SerializedValue
        {
            public enum Type
            {
                FLOAT,
                INT,
                BOOLEAN,
                STRING,
                VECTOR3,
                VECTOR2,
                VECTOR3INT,
                VECTOR2INT,
                QUATERNION
            }

            public Type valueType;

            public byte[] serializedValue;

            public object unserializedValue;

            public SerializedValue(){}

            public SerializedValue(float value)
            {
                valueType = Type.FLOAT;

                serializedValue = BitConverter.GetBytes(value);

                unserializedValue = value;
            }

            public SerializedValue(int value)
            {
                valueType = Type.INT;

                serializedValue = BitConverter.GetBytes(value);

                unserializedValue = value;
            }

            public SerializedValue(bool value)
            {
                valueType = Type.BOOLEAN;

                serializedValue = BitConverter.GetBytes(value);

                unserializedValue = value;
            }

            public SerializedValue(string value)
            {
                valueType = Type.STRING;

                serializedValue = Encoding.ASCII.GetBytes(value);

                unserializedValue = value;
            }

            public byte[] GetBytes()
            {
                byte[] bytes = new byte[4 + 4 + serializedValue.Length];

                Buffer.BlockCopy(BitConverter.GetBytes(bytes.Length), 0, bytes, 0, 4);

                Buffer.BlockCopy(BitConverter.GetBytes((int)valueType), 0, bytes, 4, 4);

                Buffer.BlockCopy(serializedValue, 0, bytes, 4 + 4, serializedValue.Length);

                return bytes;
            }

            public static SerializedValue Deserialize(byte[] bytes)
            {
                byte[] valueTypeBytes = bytes[4..8];

                Type valueType = (Type)BitConverter.ToInt32(valueTypeBytes);

                switch (valueType)
                {
                    case Type.FLOAT:
                        return new SerializedValue(BitConverter.ToSingle(bytes[8..12]));
                    case Type.INT:
                        return new SerializedValue(BitConverter.ToInt32(bytes[8..12]));
                    case Type.BOOLEAN:
                        return new SerializedValue(BitConverter.ToBoolean(bytes[8..9]));
                    case Type.STRING:
                        return new SerializedValue(Encoding.ASCII.GetString(bytes[8..bytes.Length]));
                    default:
                        SerializedValue serializedValue = new SerializedValue()
                        {
                            valueType = valueType,
                            serializedValue = bytes[8..bytes.Length]
                        };

                        return serializedValue;
                }
            }
        }

        public string packetType;

        public List<SerializedValue> values = new List<SerializedValue>();

        public Connection.PacketReliability reliability;

        public Packet(string _packetType)
        {
            packetType = _packetType;
        }

        public Packet(byte[] bytes)
        {
            byte[] packetLengthBytes = bytes[0..4];

            int packetLength = BitConverter.ToInt32(packetLengthBytes);

            byte[] packetTypeLengthBytes = bytes[4..8];
            int packetTypeLength = BitConverter.ToInt32(packetTypeLengthBytes);

            byte[] packetTypeBytes = bytes[8..(8 + packetTypeLength)];
            packetType = Encoding.ASCII.GetString(packetTypeBytes);

            byte[] payload = bytes[(8 + packetTypeLength)..bytes.Length];
            // payload = SevenZip.Compression.LZMA.SevenZipHelper.Decompress(payload);

            for (int i = 0; i < payload.Length;)
            {
                byte[] valueLengthBytes = payload[i..(i + 4)];
                int valueLength = BitConverter.ToInt32(valueLengthBytes);

                byte[] valueBytes = payload[i..(i + valueLength)];

                SerializedValue value = SerializedValue.Deserialize(valueBytes);

                values.Add(value);

                i += valueLength;
            }
        }

        public Packet(byte[] bytes, Connection.PacketReliability _reliability)
        {
            reliability = _reliability;

            byte[] packetLengthBytes = bytes[0..4];

            int packetLength = BitConverter.ToInt32(packetLengthBytes);

            byte[] packetTypeLengthBytes = bytes[4..8];
            int packetTypeLength = BitConverter.ToInt32(packetTypeLengthBytes);

            byte[] packetTypeBytes = bytes[8..(8 + packetTypeLength)];
            packetType = Encoding.ASCII.GetString(packetTypeBytes);

            byte[] payload = bytes[(8 + packetTypeLength)..bytes.Length];
            // payload = SevenZip.Compression.LZMA.SevenZipHelper.Decompress(payload);

            for (int i = 0; i < payload.Length;)
            {
                byte[] valueLengthBytes = payload[i..(i + 4)];
                int valueLength = BitConverter.ToInt32(valueLengthBytes);

                byte[] valueBytes = payload[i..(i + valueLength)];

                SerializedValue value = SerializedValue.Deserialize(valueBytes);

                values.Add(value);

                i += valueLength;
            }
        }

        public Packet AddValue(float value)
        {
            values.Add(new SerializedValue(value));

            return this;
        }

        public Packet AddValue(int value)
        {
            values.Add(new SerializedValue(value));

            return this;
        }

        public Packet AddValue(bool value)
        {
            values.Add(new SerializedValue(value));

            return this;
        }

        public Packet AddValue(string value)
        {
            values.Add(new SerializedValue(value));

            return this;
        }

        public byte[] ToBytes()
        {
            int packetLength = 4 + 4;

            byte[] packetTypeBytes = Encoding.ASCII.GetBytes(packetType);
            byte[] packetTypeLengthBytes = BitConverter.GetBytes(packetTypeBytes.Length);

            packetLength += packetTypeBytes.Length;

            byte[] payload = new byte[0];

            foreach (SerializedValue serializedValue in values)
            {
                byte[] valueBytes = serializedValue.GetBytes();

                byte[] extendedPayload = new byte[payload.Length + valueBytes.Length];

                Buffer.BlockCopy(payload, 0, extendedPayload, 0, payload.Length);
                Buffer.BlockCopy(valueBytes, 0, extendedPayload, payload.Length, valueBytes.Length);

                payload = extendedPayload;
            }

            // payload = SevenZip.Compression.LZMA.SevenZipHelper.Compress(payload);

            packetLength += payload.Length;

            byte[] bytes = new byte[packetLength];

            Buffer.BlockCopy(BitConverter.GetBytes(packetLength), 0, bytes, 0, 4);

            Buffer.BlockCopy(packetTypeLengthBytes, 0, bytes, 4, 4);
            Buffer.BlockCopy(packetTypeBytes, 0, bytes, 8, packetTypeBytes.Length);
            Buffer.BlockCopy(payload, 0, bytes, 8 + packetTypeBytes.Length, payload.Length);

            return bytes;
        }

        public float GetFloat(int index)
        {
            return (float)values[index].unserializedValue;
        }

        public int GetInt(int index)
        {
            return (int)values[index].unserializedValue;
        }

        public bool GetBool(int index)
        {
            return (bool)values[index].unserializedValue;
        }

        public string GetString(int index)
        {
            return (string)values[index].unserializedValue;
        }
    }
}