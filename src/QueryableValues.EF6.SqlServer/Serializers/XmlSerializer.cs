﻿using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Xml;

#if NETSTANDARD2_1_OR_GREATER || NET472_OR_GREATER || NET
using Microsoft.Extensions.ObjectPool;
#endif

namespace BlazarTech.QueryableValues.Serializers
{
    internal sealed class XmlSerializer
    {
        private const int ValueBufferLength = 128;

        private static readonly char[] EmptyCharArray = new char[0];

        private static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;

#if NETSTANDARD2_1_OR_GREATER || NET472_OR_GREATER || NET
        private static readonly DefaultObjectPool<StringBuilder> StringBuilderPool = new DefaultObjectPool<StringBuilder>(
            new StringBuilderPooledObjectPolicy
            {
                InitialCapacity = 512,
                MaximumRetainedCapacity = 524288
            });

#else
        private static readonly DummyStringBuilderPool StringBuilderPool = new DummyStringBuilderPool();
#endif

        private static readonly ArrayPool<char> BufferPool = ArrayPool<char>.Shared;

        private static readonly Func<IEnumerable<byte>, string> GetXmlByte = (IEnumerable<byte> values) => GetXml(values, WriteValue, 3);
        private static readonly Func<IEnumerable<short>, string> GetXmlInt16 = (IEnumerable<short> values) => GetXml(values, WriteValue, 3);
        private static readonly Func<IEnumerable<int>, string> GetXmlInt32 = (IEnumerable<int> values) => GetXml(values, WriteValue, 5);
        private static readonly Func<IEnumerable<long>, string> GetXmlInt64 = (IEnumerable<long> values) => GetXml(values, WriteValue, 10);
        private static readonly Func<IEnumerable<decimal>, string> GetXmlDecimal = (IEnumerable<decimal> values) => GetXml(values, WriteValue, 10);
        private static readonly Func<IEnumerable<float>, string> GetXmlSingle = (IEnumerable<float> values) => GetXml(values, WriteValue, 10);
        private static readonly Func<IEnumerable<double>, string> GetXmlDouble = (IEnumerable<double> values) => GetXml(values, WriteValue, 10);
        private static readonly Func<IEnumerable<DateTime>, string> GetXmlDateTime = (IEnumerable<DateTime> values) => GetXml(values, WriteValue, 27);
        private static readonly Func<IEnumerable<DateTimeOffset>, string> GetXmlDateTimeOffset = (IEnumerable<DateTimeOffset> values) => GetXml(values, WriteValue, 33);
        private static readonly Func<IEnumerable<Guid>, string> GetXmlGuid = (IEnumerable<Guid> values) => GetXml(values, WriteValue, 36);

        private sealed class WriterHelper : IDisposable
        {
            public readonly StringBuilder Sb;
            public readonly char[] Buffer;

            public WriterHelper(int bufferLength)
            {
                Sb = StringBuilderPool.Get();
                Buffer = bufferLength > 0 ? BufferPool.Rent(bufferLength) : EmptyCharArray;
            }

            public void Dispose()
            {
                if (Buffer.Length > 0)
                {
                    BufferPool.Return(Buffer);
                }

                StringBuilderPool.Return(Sb);
            }
        }

        private static void EnsureCapacity<T>(StringBuilder sb, IEnumerable<T> values, int valueMinLength)
        {
            if (valueMinLength > 0 && values.TryGetNonEnumeratedCount(out int count))
            {
                var capacity = ((valueMinLength + 7) * count) + 7;
                sb.EnsureCapacity(capacity);
            }
        }

        private static string GetXml<T>(
            IEnumerable<T> values,
            Action<WriterHelper, T> writeValue,
            int valueMinLength = 0
            )
        {
            using var writer = new WriterHelper(ValueBufferLength);

            EnsureCapacity(writer.Sb, values, valueMinLength);

            var hasValues = false;

            foreach (var value in values)
            {
                if (!hasValues)
                {
                    writer.Sb.Append("<R>");
                    hasValues = true;
                }

                writer.Sb.Append("<V>");
                writeValue(writer, value);
                writer.Sb.Append("</V>");
            }

            if (hasValues)
            {
                writer.Sb.Append("</R>");
            }
            else
            {
                writer.Sb.Append("<R />");
            }

            return writer.Sb.ToString();
        }

        private static void WriteValue(WriterHelper writer, byte value) => writer.Sb.Append(value);

        private static void WriteValue(WriterHelper writer, short value) => writer.Sb.Append(value);

        private static void WriteValue(WriterHelper writer, int value) => writer.Sb.Append(value);

        private static void WriteValue(WriterHelper writer, long value) => writer.Sb.Append(value);

        private static void WriteValue(WriterHelper writer, decimal value) => WriteFormattedValue(writer, value);

        // https://github.com/dotnet/runtime/blob/v6.0.2/src/libraries/System.Private.Xml/src/System/Xml/XmlConvert.cs#L726
        private static void WriteValue(WriterHelper writer, float value)
        {
            if (float.IsNegativeInfinity(value))
            {
                writer.Sb.Append("-INF");
            }
            else if (float.IsPositiveInfinity(value))
            {
                writer.Sb.Append("INF");
            }
            else if (IsNegativeZero(value))
            {
                writer.Sb.Append("-0");
            }
            else
            {
                WriteFormattedValue(writer, value, "R");
            }
        }

        // https://github.com/dotnet/runtime/blob/v6.0.2/src/libraries/System.Private.Xml/src/System/Xml/XmlConvert.cs#L737
        private static void WriteValue(WriterHelper writer, double value)
        {
            if (double.IsNegativeInfinity(value))
            {
                writer.Sb.Append("-INF");
            }
            else if (double.IsPositiveInfinity(value))
            {
                writer.Sb.Append("INF");
            }
            else if (IsNegativeZero(value))
            {
                writer.Sb.Append("-0");
            }
            else
            {
                WriteFormattedValue(writer, value, "R");
            }
        }

        private static void WriteValue(WriterHelper writer, DateTime value)
        {
            if (value.Kind != DateTimeKind.Unspecified)
            {
                value = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
            }

            WriteFormattedValue(writer, value, "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'FFFFFFFK");
        }

        private static void WriteValue(WriterHelper writer, DateTimeOffset value)
        {
            if (value.Offset == TimeSpan.Zero)
            {
                WriteFormattedValue(writer, value, "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'FFFFFFF'Z'");
            }
            else
            {
                WriteFormattedValue(writer, value, "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'FFFFFFFK");
            }
        }

        private static void WriteValue(WriterHelper writer, Guid value) => WriteFormattedValue(writer, value);

        private static void WriteValue(XmlWriter writer, char[] chars, int length)
        {
            var startIndex = 0;
            var localLength = 0;

            for (int i = 0; i < length; i++)
            {
                var c = chars[i];
                var isValidCharacter = XmlConvert.IsXmlChar(c);
                var mustEntitize = isValidCharacter && (char.IsWhiteSpace(c) || char.IsControl(c));

                if (mustEntitize)
                {
                    WriterHelper(writer, chars, startIndex, ref localLength);
                    writer.WriteCharEntity(c);
                    startIndex = i + 1;
                }
                else if (isValidCharacter)
                {
                    localLength++;
                }
                else if (
                    i + 1 < length &&
                    // todo: Do I have to worry about endianness here?
                    XmlConvert.IsXmlSurrogatePair(chars[i + 1], chars[i])
                    )
                {
                    localLength += 2;
                    i++;
                }
                // It is an illegal XML character.
                // https://www.w3.org/TR/xml/#charsets
                else
                {
                    localLength++;
                    chars[i] = '?';
                }
            }

            WriterHelper(writer, chars, startIndex, ref localLength);

            static void WriterHelper(XmlWriter writer, char[] chars, int startIndex, ref int length)
            {
                if (length > 0)
                {
                    writer.WriteChars(chars, startIndex, length);
                    length = 0;
                }
            }
        }

#if NET6_0_OR_GREATER
        // https://github.com/dotnet/runtime/blob/v6.0.2/src/libraries/System.Private.CoreLib/src/System/Text/StringBuilder.cs#L1176
        private static void WriteFormattedValue<T>(WriterHelper writer, T value, ReadOnlySpan<char> format = default)
            where T : ISpanFormattable
        {
            if (value.TryFormat(writer.Buffer, out int charsWritten, format: format, provider: InvariantCulture))
            {
                writer.Sb.Append(writer.Buffer, 0, charsWritten);
            }
            else
            {
                throw new Exception("Should not happen.");
            }
        }
#else
        private static void WriteFormattedValue<T>(WriterHelper writer, T value, string? format = default)
            where T : IFormattable
        {
            var formattedString = value.ToString(format, InvariantCulture);
            writer.Sb.Append(formattedString);
        }
#endif

        // https://github.com/dotnet/runtime/blob/v6.0.2/src/libraries/System.Private.Xml/src/System/Xml/XmlConvert.cs#L1459
        private static bool IsNegativeZero(double value)
        {
            // Simple equals function will report that -0 is equal to +0, so compare bits instead
            return
                value == 0 &&
                BitConverter.DoubleToInt64Bits(value) == BitConverter.DoubleToInt64Bits(-0e0);
        }

        public string Serialize(IEnumerable<byte> values)
        {
            return GetXmlByte(values);
        }

        public string Serialize(IEnumerable<short> values)
        {
            return GetXmlInt16(values);
        }

        public string Serialize(IEnumerable<int> values)
        {
            return GetXmlInt32(values);
        }

        public string Serialize(IEnumerable<long> values)
        {
            return GetXmlInt64(values);
        }

        public string Serialize(IEnumerable<decimal> values)
        {
            return GetXmlDecimal(values);
        }

        public string Serialize(IEnumerable<float> values)
        {
            return GetXmlSingle(values);
        }

        public string Serialize(IEnumerable<double> values)
        {
            return GetXmlDouble(values);
        }

        public string Serialize(IEnumerable<DateTime> values)
        {
            return GetXmlDateTime(values);
        }

        public string Serialize(IEnumerable<DateTimeOffset> values)
        {
            return GetXmlDateTimeOffset(values);
        }

        public string Serialize(IEnumerable<Guid> values)
        {
            return GetXmlGuid(values);
        }

        private static XmlWriter CreateXmlWriter(StringBuilder sb)
        {
            var settings = new XmlWriterSettings
            {
                CheckCharacters = false,
                ConformanceLevel = ConformanceLevel.Fragment
            };

            return XmlWriter.Create(sb, settings);
        }

        public string Serialize(IEnumerable<char> values)
        {
            var sb = StringBuilderPool.Get();
            var buffer = BufferPool.Rent(1);

            try
            {
                EnsureCapacity(sb, values, 1);

                using (var writer = CreateXmlWriter(sb))
                {
                    writer.WriteStartElement("R");

                    foreach (var value in values)
                    {
                        buffer[0] = value;

                        writer.WriteStartElement("V");
                        WriteValue(writer, buffer, 1);
                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();
                }

                return sb.ToString();
            }
            finally
            {
                BufferPool.Return(buffer);
                StringBuilderPool.Return(sb);
            }
        }

        public string Serialize(IEnumerable<string> values)
        {
            const int defaultBufferLength = 25;

            var sb = StringBuilderPool.Get();

            try
            {
                EnsureCapacity(sb, values, defaultBufferLength);

                using (var writer = CreateXmlWriter(sb))
                {
                    writer.WriteStartElement("R");

                    var buffer = BufferPool.Rent(defaultBufferLength);

                    // buffer.Length may be bigger than defaultBufferLength.
                    var lastLength = buffer.Length;

                    try
                    {
                        foreach (var value in values)
                        {
                            if (value is null)
                            {
                                continue;
                            }

                            if (value.Length > lastLength)
                            {
                                BufferPool.Return(buffer);
                                buffer = BufferPool.Rent(value.Length);
                                lastLength = buffer.Length;
                            }

                            value.CopyTo(0, buffer, 0, value.Length);

                            writer.WriteStartElement("V");
                            WriteValue(writer, buffer, value.Length);
                            writer.WriteEndElement();
                        }
                    }
                    finally
                    {
                        BufferPool.Return(buffer);
                    }

                    writer.WriteEndElement();
                }

                return sb.ToString();
            }
            finally
            {
                StringBuilderPool.Return(sb);
            }
        }

#if NET452
        private class DummyStringBuilderPool
        {
            public StringBuilder Get() => new StringBuilder();
            public void Return(StringBuilder _) { }
        }
#endif
    }
}
