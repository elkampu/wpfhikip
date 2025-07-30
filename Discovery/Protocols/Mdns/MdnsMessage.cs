using System.Text;

namespace wpfhikip.Discovery.Protocols.Mdns
{
    /// <summary>
    /// Correctly formatted mDNS message handling
    /// </summary>
    public class MdnsMessage
    {
        public ushort TransactionId { get; set; }
        public ushort Flags { get; set; }
        public List<MdnsRecord> Questions { get; set; } = new();
        public List<MdnsRecord> Answers { get; set; } = new();
        public List<MdnsRecord> Authority { get; set; } = new();
        public List<MdnsRecord> Additional { get; set; } = new();

        /// <summary>
        /// Creates a proper mDNS query for multiple services
        /// </summary>
        public static MdnsMessage CreateQuery(params string[] serviceTypes)
        {
            var message = new MdnsMessage
            {
                TransactionId = 0, // mDNS uses 0 for queries
                Flags = 0x0000 // Standard query flags
            };

            foreach (var serviceType in serviceTypes)
            {
                message.Questions.Add(new MdnsRecord
                {
                    Name = serviceType,
                    Type = MdnsRecordType.PTR,
                    Class = MdnsRecordClass.IN
                });
            }

            return message;
        }

        /// <summary>
        /// Parses mDNS message from bytes
        /// </summary>
        public static MdnsMessage? Parse(byte[] data)
        {
            if (data == null || data.Length < 12) return null;

            try
            {
                var message = new MdnsMessage
                {
                    TransactionId = ReadUInt16(data, 0),
                    Flags = ReadUInt16(data, 2)
                };

                // Read counts from header
                var questionCount = ReadUInt16(data, 4);
                var answerCount = ReadUInt16(data, 6);
                var authorityCount = ReadUInt16(data, 8);
                var additionalCount = ReadUInt16(data, 10);

                int offset = 12; // Skip header

                // Parse sections
                ParseRecords(data, ref offset, questionCount, message.Questions, true);
                ParseRecords(data, ref offset, answerCount, message.Answers, false);
                ParseRecords(data, ref offset, authorityCount, message.Authority, false);
                ParseRecords(data, ref offset, additionalCount, message.Additional, false);

                return message;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing mDNS message: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Converts to properly formatted byte array (FIXED)
        /// </summary>
        public byte[] ToByteArray()
        {
            var result = new List<byte>();

            // Header (12 bytes) - Properly formatted for mDNS
            WriteUInt16(result, TransactionId);                    // Transaction ID (big-endian)
            WriteUInt16(result, Flags);                           // Flags (big-endian)
            WriteUInt16(result, (ushort)Questions.Count);         // Question count
            WriteUInt16(result, (ushort)Answers.Count);           // Answer count
            WriteUInt16(result, (ushort)Authority.Count);         // Authority count
            WriteUInt16(result, (ushort)Additional.Count);        // Additional count

            // Questions section
            foreach (var question in Questions)
            {
                result.AddRange(EncodeName(question.Name));
                WriteUInt16(result, (ushort)question.Type);        // Type (big-endian)
                WriteUInt16(result, (ushort)question.Class);       // Class (big-endian)
            }

            // Answers section
            foreach (var answer in Answers)
            {
                result.AddRange(EncodeName(answer.Name));
                WriteUInt16(result, (ushort)answer.Type);
                WriteUInt16(result, (ushort)answer.Class);
                WriteUInt32(result, answer.TTL);

                var dataBytes = EncodeRecordData(answer);
                WriteUInt16(result, (ushort)dataBytes.Length);
                result.AddRange(dataBytes);
            }

            var resultArray = result.ToArray();

            // Debug output with better formatting
            System.Diagnostics.Debug.WriteLine($"mDNS query: {Questions.Count} questions, {resultArray.Length} bytes");
            System.Diagnostics.Debug.WriteLine($"mDNS services: {string.Join(", ", Questions.Select(q => q.Name))}");
            System.Diagnostics.Debug.WriteLine($"mDNS hex: {BitConverter.ToString(resultArray)}");

            return resultArray;
        }

        // Helper method to write big-endian 16-bit values
        private static void WriteUInt16(List<byte> buffer, ushort value)
        {
            buffer.Add((byte)(value >> 8));    // High byte first (big-endian)
            buffer.Add((byte)(value & 0xFF));  // Low byte second
        }

        // Helper method to write big-endian 32-bit values
        private static void WriteUInt32(List<byte> buffer, uint value)
        {
            buffer.Add((byte)(value >> 24));
            buffer.Add((byte)(value >> 16));
            buffer.Add((byte)(value >> 8));
            buffer.Add((byte)(value & 0xFF));
        }

        private static void ParseRecords(byte[] data, ref int offset, int count, List<MdnsRecord> records, bool isQuestion)
        {
            for (int i = 0; i < count && offset < data.Length; i++)
            {
                var record = ParseRecord(data, ref offset, isQuestion);
                if (record != null) records.Add(record);
            }
        }

        private static MdnsRecord? ParseRecord(byte[] data, ref int offset, bool isQuestion)
        {
            try
            {
                var record = new MdnsRecord
                {
                    Name = ParseName(data, ref offset),
                    Type = (MdnsRecordType)ReadUInt16(data, ref offset),
                    Class = (MdnsRecordClass)ReadUInt16(data, ref offset)
                };

                if (!isQuestion && offset + 6 <= data.Length)
                {
                    record.TTL = ReadUInt32(data, ref offset);
                    var dataLength = ReadUInt16(data, ref offset);
                    if (offset + dataLength <= data.Length)
                    {
                        record.Data = ParseRecordData(data, offset, dataLength, record.Type);
                        offset += dataLength;
                    }
                }

                return record;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing mDNS record: {ex.Message}");
                return null;
            }
        }

        private static string ParseName(byte[] data, ref int offset)
        {
            var labels = new List<string>();
            var jumped = false;
            var originalOffset = offset;

            while (offset < data.Length)
            {
                var length = data[offset];
                if (length == 0)
                {
                    offset++;
                    break;
                }

                if ((length & 0xC0) == 0xC0) // Compression
                {
                    if (!jumped) originalOffset = offset + 2;
                    offset = ((length & 0x3F) << 8) | data[offset + 1];
                    jumped = true;
                    continue;
                }

                offset++;
                if (offset + length <= data.Length)
                {
                    labels.Add(Encoding.UTF8.GetString(data, offset, length));
                    offset += length;
                }
            }

            if (jumped) offset = originalOffset;
            return string.Join(".", labels);
        }

        private static string? ParseRecordData(byte[] data, int offset, int length, MdnsRecordType type)
        {
            try
            {
                return type switch
                {
                    MdnsRecordType.A when length == 4 =>
                        $"{data[offset]}.{data[offset + 1]}.{data[offset + 2]}.{data[offset + 3]}",
                    MdnsRecordType.PTR => ParseName(data, ref offset),
                    MdnsRecordType.TXT => ParseTxtRecord(data, offset, length),
                    MdnsRecordType.SRV when length >= 6 => ParseSrvRecord(data, offset, length),
                    _ => BitConverter.ToString(data, offset, Math.Min(length, 32))
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing record data: {ex.Message}");
                return null;
            }
        }

        private static string ParseTxtRecord(byte[] data, int offset, int length)
        {
            var values = new List<string>();
            int pos = offset;
            int end = offset + length;

            while (pos < end)
            {
                var len = data[pos];
                pos++;
                if (pos + len <= end)
                {
                    values.Add(Encoding.UTF8.GetString(data, pos, len));
                    pos += len;
                }
                else break;
            }

            return string.Join(";", values);
        }

        private static string ParseSrvRecord(byte[] data, int offset, int length)
        {
            var priority = ReadUInt16(data, offset);
            var weight = ReadUInt16(data, offset + 2);
            var port = ReadUInt16(data, offset + 4);
            int nameOffset = offset + 6;
            var target = ParseName(data, ref nameOffset);
            return $"{priority},{weight},{port},{target}";
        }

        private static byte[] EncodeName(string name)
        {
            var result = new List<byte>();
            if (string.IsNullOrEmpty(name))
            {
                result.Add(0);
                return result.ToArray();
            }

            var parts = name.Split('.', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var bytes = Encoding.UTF8.GetBytes(part);
                if (bytes.Length > 63)
                {
                    System.Diagnostics.Debug.WriteLine($"mDNS: Label too long: {part} ({bytes.Length} bytes)");
                    continue; // Skip invalid labels
                }
                result.Add((byte)bytes.Length);
                result.AddRange(bytes);
            }
            result.Add(0); // Null terminator
            return result.ToArray();
        }

        private static byte[] EncodeRecordData(MdnsRecord record)
        {
            if (string.IsNullOrEmpty(record.Data)) return Array.Empty<byte>();

            try
            {
                return record.Type switch
                {
                    MdnsRecordType.A => record.Data.Split('.').Select(byte.Parse).ToArray(),
                    MdnsRecordType.PTR => EncodeName(record.Data),
                    MdnsRecordType.TXT => Encoding.UTF8.GetBytes(record.Data),
                    _ => Encoding.UTF8.GetBytes(record.Data)
                };
            }
            catch
            {
                return Array.Empty<byte>();
            }
        }

        private static ushort ReadUInt16(byte[] data, int offset)
        {
            return (ushort)((data[offset] << 8) | data[offset + 1]);
        }

        private static ushort ReadUInt16(byte[] data, ref int offset)
        {
            var value = ReadUInt16(data, offset);
            offset += 2;
            return value;
        }

        private static uint ReadUInt32(byte[] data, ref int offset)
        {
            var value = (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
            offset += 4;
            return value;
        }
    }

    public class MdnsRecord
    {
        public string Name { get; set; } = string.Empty;
        public MdnsRecordType Type { get; set; }
        public MdnsRecordClass Class { get; set; }
        public uint TTL { get; set; }
        public string? Data { get; set; }
    }

    public enum MdnsRecordType : ushort
    {
        A = 1,
        NS = 2,
        CNAME = 5,
        PTR = 12,
        TXT = 16,
        AAAA = 28,
        SRV = 33
    }

    public enum MdnsRecordClass : ushort
    {
        IN = 1
    }
}