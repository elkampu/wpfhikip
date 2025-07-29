using System.Text;

namespace wpfhikip.Discovery.Protocols.Mdns
{
    /// <summary>
    /// Represents an mDNS message
    /// </summary>
    public class MdnsMessage
    {
        public ushort TransactionId { get; set; }
        public bool IsResponse { get; set; }
        public bool IsAuthoritativeAnswer { get; set; }
        public bool IsTruncated { get; set; }
        public bool IsRecursionDesired { get; set; }
        public bool IsRecursionAvailable { get; set; }

        public List<MdnsRecord> Questions { get; set; } = new();
        public List<MdnsRecord> Answers { get; set; } = new();
        public List<MdnsRecord> Authority { get; set; } = new();
        public List<MdnsRecord> Additional { get; set; } = new();

        /// <summary>
        /// Creates an mDNS query for a service type
        /// </summary>
        public static MdnsMessage CreateQuery(string serviceType)
        {
            var message = new MdnsMessage
            {
                TransactionId = 0, // mDNS uses 0 for queries
                IsResponse = false,
                IsRecursionDesired = false
            };

            message.Questions.Add(new MdnsRecord
            {
                Name = serviceType,
                Type = MdnsRecordType.PTR,
                Class = MdnsRecordClass.IN
            });

            return message;
        }

        /// <summary>
        /// Parses mDNS message from byte array
        /// </summary>
        public static MdnsMessage? Parse(byte[] data)
        {
            if (data.Length < 12)
                return null;

            try
            {
                var message = new MdnsMessage();
                int offset = 0;

                // Parse header
                message.TransactionId = (ushort)((data[offset] << 8) | data[offset + 1]);
                offset += 2;

                var flags = (ushort)((data[offset] << 8) | data[offset + 1]);
                offset += 2;

                message.IsResponse = (flags & 0x8000) != 0;
                message.IsAuthoritativeAnswer = (flags & 0x0400) != 0;
                message.IsTruncated = (flags & 0x0200) != 0;
                message.IsRecursionDesired = (flags & 0x0100) != 0;
                message.IsRecursionAvailable = (flags & 0x0080) != 0;

                var questionCount = (ushort)((data[offset] << 8) | data[offset + 1]);
                offset += 2;
                var answerCount = (ushort)((data[offset] << 8) | data[offset + 1]);
                offset += 2;
                var authorityCount = (ushort)((data[offset] << 8) | data[offset + 1]);
                offset += 2;
                var additionalCount = (ushort)((data[offset] << 8) | data[offset + 1]);
                offset += 2;

                // Parse sections (simplified parsing)
                for (int i = 0; i < questionCount && offset < data.Length; i++)
                {
                    var record = ParseRecord(data, ref offset, true);
                    if (record != null)
                        message.Questions.Add(record);
                }

                for (int i = 0; i < answerCount && offset < data.Length; i++)
                {
                    var record = ParseRecord(data, ref offset, false);
                    if (record != null)
                        message.Answers.Add(record);
                }

                return message;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Converts message to byte array
        /// </summary>
        public byte[] ToByteArray()
        {
            var result = new List<byte>();

            // Header
            result.AddRange(BitConverter.GetBytes((ushort)((TransactionId << 8) | (TransactionId >> 8))));

            ushort flags = 0;
            if (IsRecursionDesired) flags |= 0x0001;
            result.AddRange(BitConverter.GetBytes((ushort)((flags << 8) | (flags >> 8))));

            result.AddRange(BitConverter.GetBytes((ushort)(((ushort)Questions.Count << 8) | ((ushort)Questions.Count >> 8))));
            result.AddRange(BitConverter.GetBytes((ushort)0)); // Answer count
            result.AddRange(BitConverter.GetBytes((ushort)0)); // Authority count
            result.AddRange(BitConverter.GetBytes((ushort)0)); // Additional count

            // Questions
            foreach (var question in Questions)
            {
                result.AddRange(EncodeNameAsLabels(question.Name));
                result.AddRange(BitConverter.GetBytes((ushort)(((ushort)question.Type << 8) | ((ushort)question.Type >> 8))));
                result.AddRange(BitConverter.GetBytes((ushort)(((ushort)question.Class << 8) | ((ushort)question.Class >> 8))));
            }

            return result.ToArray();
        }

        /// <summary>
        /// Parses a DNS record from byte array
        /// </summary>
        private static MdnsRecord? ParseRecord(byte[] data, ref int offset, bool isQuestion)
        {
            try
            {
                var record = new MdnsRecord();

                // Parse name (simplified)
                record.Name = ParseName(data, ref offset);

                if (offset + 4 > data.Length)
                    return null;

                record.Type = (MdnsRecordType)((data[offset] << 8) | data[offset + 1]);
                offset += 2;
                record.Class = (MdnsRecordClass)((data[offset] << 8) | data[offset + 1]);
                offset += 2;

                if (!isQuestion)
                {
                    if (offset + 6 > data.Length)
                        return null;

                    record.TTL = (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
                    offset += 4;

                    var dataLength = (ushort)((data[offset] << 8) | data[offset + 1]);
                    offset += 2;

                    if (offset + dataLength > data.Length)
                        return null;

                    // Parse data based on record type
                    record.Data = ParseRecordData(data, offset, dataLength, record.Type);
                    offset += dataLength;
                }

                return record;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Parses DNS name from byte array
        /// </summary>
        private static string ParseName(byte[] data, ref int offset)
        {
            var labels = new List<string>();

            while (offset < data.Length)
            {
                var length = data[offset++];

                if (length == 0)
                    break;

                if ((length & 0xC0) == 0xC0)
                {
                    // Compression pointer - not fully implemented
                    offset++;
                    break;
                }

                if (offset + length > data.Length)
                    break;

                var label = Encoding.UTF8.GetString(data, offset, length);
                labels.Add(label);
                offset += length;
            }

            return string.Join(".", labels);
        }

        /// <summary>
        /// Parses record data based on type
        /// </summary>
        private static string? ParseRecordData(byte[] data, int offset, int length, MdnsRecordType type)
        {
            try
            {
                switch (type)
                {
                    case MdnsRecordType.A:
                        if (length == 4)
                        {
                            return $"{data[offset]}.{data[offset + 1]}.{data[offset + 2]}.{data[offset + 3]}";
                        }
                        break;

                    case MdnsRecordType.PTR:
                    case MdnsRecordType.CNAME:
                        var nameOffset = offset;
                        return ParseName(data, ref nameOffset);

                    case MdnsRecordType.TXT:
                        return Encoding.UTF8.GetString(data, offset, length);

                    case MdnsRecordType.SRV:
                        if (length >= 6)
                        {
                            var priority = (ushort)((data[offset] << 8) | data[offset + 1]);
                            var weight = (ushort)((data[offset + 2] << 8) | data[offset + 3]);
                            var port = (ushort)((data[offset + 4] << 8) | data[offset + 5]);
                            var srvOffset = offset + 6;
                            var target = ParseName(data, ref srvOffset);
                            return $"{priority} {weight} {port} {target}";
                        }
                        break;
                }
            }
            catch
            {
                // Parsing error
            }

            return null;
        }

        /// <summary>
        /// Encodes a domain name as DNS labels
        /// </summary>
        private static byte[] EncodeNameAsLabels(string name)
        {
            var result = new List<byte>();
            var parts = name.Split('.');

            foreach (var part in parts)
            {
                if (!string.IsNullOrEmpty(part))
                {
                    var bytes = Encoding.UTF8.GetBytes(part);
                    result.Add((byte)bytes.Length);
                    result.AddRange(bytes);
                }
            }

            result.Add(0); // End of name
            return result.ToArray();
        }
    }

    /// <summary>
    /// Represents an mDNS record
    /// </summary>
    public class MdnsRecord
    {
        public string Name { get; set; } = string.Empty;
        public MdnsRecordType Type { get; set; }
        public MdnsRecordClass Class { get; set; }
        public uint TTL { get; set; }
        public string? Data { get; set; }
    }

    /// <summary>
    /// mDNS record types
    /// </summary>
    public enum MdnsRecordType : ushort
    {
        A = 1,
        NS = 2,
        CNAME = 5,
        SOA = 6,
        PTR = 12,
        MX = 15,
        TXT = 16,
        AAAA = 28,
        SRV = 33
    }

    /// <summary>
    /// mDNS record classes
    /// </summary>
    public enum MdnsRecordClass : ushort
    {
        IN = 1
    }
}