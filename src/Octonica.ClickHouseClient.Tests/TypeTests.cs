﻿#region License Apache 2.0
/* Copyright 2019-2021 Octonica
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Octonica.ClickHouseClient.Exceptions;
using Octonica.ClickHouseClient.Protocol;
using Octonica.ClickHouseClient.Types;
using TimeZoneConverter;
using Xunit;

namespace Octonica.ClickHouseClient.Tests
{
    public class TypeTests : ClickHouseTestsBase, IClassFixture<EncodingFixture>
    {
        [Fact]
        public async Task ReadFixedStringScalar()
        {
            await using var connection = await OpenConnectionAsync();

            await using var cmd = connection.CreateCommand("SELECT cast('1234йё' AS FixedString(10))");
            
            var result = await cmd.ExecuteScalarAsync();
            var resultBytes = Assert.IsType<byte[]>(result);

            Assert.Equal(10, resultBytes.Length);
            Assert.Equal(0, resultBytes[^1]);
            Assert.Equal(0, resultBytes[^2]);

            var resultString = Encoding.UTF8.GetString(resultBytes, 0, 8);
            Assert.Equal("1234йё", resultString);
        }

        [Fact]
        public async Task ReadFixedStringWithEncoding()
        {
            const string str = "аaбbсc";
            var encoding = Encoding.GetEncoding("windows-1251");

            await using var connection = await OpenConnectionAsync();

            await using var cmd = connection.CreateCommand($"SELECT cast(convertCharset('{str}', 'UTF8', 'windows-1251') AS FixedString(10))");

            var result = await cmd.ExecuteScalarAsync();
            var resultBytes = Assert.IsType<byte[]>(result);

            Assert.Equal(10, resultBytes.Length);
            Assert.Equal(0, resultBytes[^1]);
            Assert.Equal(0, resultBytes[^2]);
            Assert.Equal(0, resultBytes[^3]);
            Assert.Equal(0, resultBytes[^4]);
            Assert.NotEqual(0, resultBytes[^5]);

            await using var reader = await cmd.ExecuteReaderAsync();
            reader.ConfigureDataReader(new ClickHouseColumnSettings(encoding));

            var success = await reader.ReadAsync();
            Assert.True(success);

            var strResult = reader.GetString(0);
            Assert.Equal(str, strResult);

            success = await reader.ReadAsync();
            Assert.False(success);

            var error = Assert.Throws<ClickHouseException>(() => reader.ConfigureDataReader(new ClickHouseColumnSettings(Encoding.UTF8)));
            Assert.Equal(ClickHouseErrorCodes.DataReaderError, error.ErrorCode);
        }

        [Fact]
        public async Task ReadStringWithEncoding()
        {
            const string str = "АБВГДЕ";
            var encoding = Encoding.GetEncoding("windows-1251");

            await using var connection = await OpenConnectionAsync();
            await using var cmd = connection.CreateCommand($"SELECT convertCharset('{str}', 'UTF8', 'windows-1251') AS c");

            await using var reader = await cmd.ExecuteReaderAsync();
            reader.ConfigureDataReader(new ClickHouseColumnSettings(encoding));

            var success = await reader.ReadAsync();
            Assert.True(success);

            var error = Assert.Throws<ClickHouseException>(() => reader.ConfigureColumn("c", new ClickHouseColumnSettings(Encoding.UTF8)));
            Assert.Equal(ClickHouseErrorCodes.DataReaderError, error.ErrorCode);

            var strResult = reader.GetString(0);
            Assert.Equal(str, strResult);

            success = await reader.ReadAsync();
            Assert.False(success);
        }

        [Fact]
        public async Task ReadStringWithEncodingScalar()
        {
            const string str = "АБВГДЕ";
            var encoding = Encoding.GetEncoding("windows-1251");

            await using var connection = await OpenConnectionAsync();
            await using var cmd = connection.CreateCommand($"SELECT convertCharset('{str}', 'UTF8', 'windows-1251') AS c");

            var strResult = await cmd.ExecuteScalarAsync(new ClickHouseColumnSettings(encoding));
            Assert.Equal(str, strResult);
        }

        [Fact]
        public async Task ReadStringParameterScalar()
        {
            var strings = new[] {null, "", "abcde", "fghi", "jklm", "nopq", "rst", "uvwxy","z"};

            var byteArray = Encoding.UTF8.GetBytes(strings[4]!);
            var byteMemory = Encoding.UTF8.GetBytes(strings[5]!).AsMemory();
            var byteRoMemory = (ReadOnlyMemory<byte>) Encoding.UTF8.GetBytes(strings[6]!).AsMemory();
            var charMemory = strings[7]!.ToCharArray().AsMemory();
            var charArray = strings[8]!.ToCharArray();
            var values = new object?[] {strings[0], strings[1], strings[2], strings[3].AsMemory(), byteArray, byteMemory, byteRoMemory, charMemory, charArray};

            await using var connection = await OpenConnectionAsync();
            await using var cmd = connection.CreateCommand("SELECT {val}");
            var param = cmd.Parameters.AddWithValue("val", "some_value", DbType.String);
            for (var i = 0; i < values.Length; i++)
            {
                param.Value = values[i];
                var result = await cmd.ExecuteScalarAsync(CancellationToken.None);
                if (values[i] == null)
                    Assert.Equal(result, DBNull.Value);
                else
                    Assert.Equal(strings[i], Assert.IsType<string>(result));
            }
        }

        [Fact]
        public async Task ReadGuidScalar()
        {
            var guidValue = new Guid("74D47928-2423-4FE2-AD45-82E296BF6058");

            await using var connection = await OpenConnectionAsync();
            await using var cmd = connection.CreateCommand($"SELECT cast('{guidValue:D}' AS UUID)");

            var result = await cmd.ExecuteScalarAsync();
            var resultGuid = Assert.IsType<Guid>(result);

            Assert.Equal(guidValue, resultGuid);
        }

        [Fact]
        public async Task ReadDecimal128Scalar()
        {
            var testData = new[] {decimal.Zero, decimal.One, decimal.MinusOne, decimal.MinValue / 100, decimal.MaxValue / 100, decimal.One / 100, decimal.MinusOne / 100};
            
            await using var connection = await OpenConnectionAsync();

            foreach (var testValue in testData)
            {
                await using var cmd = connection.CreateCommand($"SELECT cast('{testValue.ToString(CultureInfo.InvariantCulture)}' AS Decimal128(2))");

                var result = await cmd.ExecuteScalarAsync();
                var resultDecimal = Assert.IsType<decimal>(result);

                Assert.Equal(testValue, resultDecimal);
            }

            await using (var cmd2 = connection.CreateCommand("SELECT cast('-108.4815162342' AS Decimal128(35))"))
            {
                var result2 = await cmd2.ExecuteScalarAsync<decimal>();
                Assert.Equal(-108.4815162342m, result2);
            }

            await using (var cmd2 = connection.CreateCommand("SELECT cast('-999.9999999999999999999999999' AS Decimal128(35))"))
            {
                var result2 = await cmd2.ExecuteScalarAsync<decimal>();
                Assert.Equal(-999.9999999999999999999999999m, result2);
            }
        }

        [Fact]
        public async Task ReadDecimal64Scalar()
        {
            var testData = new[] { decimal.Zero, decimal.One, decimal.MinusOne, 999_999_999_999_999.999m, -999_999_999_999_999.999m, decimal.One / 1000, decimal.MinusOne / 1000 };

            await using var connection = await OpenConnectionAsync();

            foreach (var testValue in testData)
            {
                await using var cmd = connection.CreateCommand($"SELECT cast('{testValue.ToString(CultureInfo.InvariantCulture)}' AS Decimal64(3))");

                var result = await cmd.ExecuteScalarAsync();
                var resultDecimal = Assert.IsType<decimal>(result);

                Assert.Equal(testValue, resultDecimal);
            }
        }

        [Fact]
        public async Task ReadDecimal32Scalar()
        {
            var testData = new[] { decimal.Zero, decimal.One, decimal.MinusOne, 9.9999999m, -9.9999999m, decimal.One / 100_000_000, decimal.MinusOne / 100_000_000 };

            await using var connection = await OpenConnectionAsync();

            foreach (var testValue in testData)
            {
                await using var cmd = connection.CreateCommand($"SELECT cast('{testValue.ToString(CultureInfo.InvariantCulture)}' AS Decimal32(8))");

                var result = await cmd.ExecuteScalarAsync();
                var resultDecimal = Assert.IsType<decimal>(result);

                Assert.Equal(testValue, resultDecimal);
            }
        }

        [Fact]
        public async Task ReadDateTimeScalar()
        {
            await using var connection = await OpenConnectionAsync();

            await using var cmd = connection.CreateCommand("SELECT cast('2015-04-21 14:59:44' AS DateTime)");

            var result = await cmd.ExecuteScalarAsync();
            var resultDateTime = Assert.IsType<DateTimeOffset>(result);

            Assert.Equal(new DateTime(2015, 4, 21, 14, 59, 44), resultDateTime.DateTime);
        }

        [Fact]
        public async Task ReadDateTimeWithTimezoneScalar()
        {
            await using var connection = await OpenConnectionAsync();

            var tzName = TimeZoneInfo.Local.Id;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                tzName = TZConvert.WindowsToIana(TimeZoneInfo.Local.Id);

            await using var cmd = connection.CreateCommand($"SELECT toDateTime('2015-04-21 14:59:44', '{tzName}')");

            var result = await cmd.ExecuteScalarAsync();
            var resultDateTime = Assert.IsType<DateTimeOffset>(result);

            Assert.Equal(new DateTime(2015, 4, 21, 14, 59, 44), resultDateTime);
        }

        [Fact]
        public async Task ReadDateTime64Scalar()
        {
            await using var connection = await OpenConnectionAsync();

            await using var cmd = connection.CreateCommand("SELECT cast('2015-04-21 14:59:44.12345' AS DateTime64)");

            var result = await cmd.ExecuteScalarAsync();
            var resultDateTime = Assert.IsType<DateTimeOffset>(result);

            Assert.Equal(new DateTime(2015, 4, 21, 14, 59, 44).AddMilliseconds(123), resultDateTime.DateTime);
        }

        [Fact]
        public async Task ReadDateTime64WithTimezoneScalar()
        {
            await using var connection = await OpenConnectionAsync();

            var tzName = TimeZoneInfo.Local.Id;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                tzName = TZConvert.WindowsToIana(TimeZoneInfo.Local.Id);

            await using var cmd = connection.CreateCommand($"SELECT cast('2015-04-21 14:59:44.123456789' AS DateTime64(9,'{tzName}'))");

            var result = await cmd.ExecuteScalarAsync();
            var resultDateTime = Assert.IsType<DateTimeOffset>(result);

            Assert.Equal(new DateTime(2015, 4, 21, 14, 59, 44).Add(TimeSpan.FromMilliseconds(123.4567)), resultDateTime);
        }

        [Fact]
        public async Task ReadDateTime64ParameterScalar()
        {
            var values = new[]
            {
                default,
                DateTime.UnixEpoch.Add(TimeSpan.FromMilliseconds(1111.1111)),
                new DateTime(2531, 3, 5, 7, 9, 23).Add(TimeSpan.FromMilliseconds(123.45)),
                new DateTime(1984, 4, 21, 14, 59, 44).Add(TimeSpan.FromMilliseconds(123.4567))
            };

            await using var connection = await OpenConnectionAsync();
            var timeZone = connection.GetServerTimeZone();

            await using var cmd = connection.CreateCommand("SELECT {v}");
            var parameter = new ClickHouseParameter("v") {ClickHouseDbType = ClickHouseDbType.DateTime64};
            cmd.Parameters.Add(parameter);

            for (int precision = 0; precision < 10; precision++)
            {
                parameter.Precision = (byte) precision;
                var div = TimeSpan.TicksPerSecond / (long) Math.Pow(10, precision);
                if (div == 0)
                    div = -(long) Math.Pow(10, precision) / TimeSpan.TicksPerSecond;

                foreach (var value in values)
                {
                    parameter.Value = value;

                    var result = await cmd.ExecuteScalarAsync();
                    var resultDateTime = Assert.IsType<DateTimeOffset>(result);

                    DateTime expectedValue;
                    if (value == default)
                        expectedValue = default;
                    else if (div > 0)
                        expectedValue = new DateTime(value.Ticks / div * div);
                    else
                        expectedValue = value;

                    Assert.Equal(expectedValue, resultDateTime.DateTime);
                }

                // Min and max values must be adjusted to the server's timezone

                var unixEpochOffset = timeZone.GetUtcOffset(DateTime.UnixEpoch);
                var unixEpochValue = new DateTime(DateTime.UnixEpoch.Ticks + unixEpochOffset.Ticks);
                parameter.Value = unixEpochValue;

                var unixEpochResult = await cmd.ExecuteScalarAsync();
                var unixEpochResultDto = Assert.IsType<DateTimeOffset>(unixEpochResult);

                Assert.Equal(default, unixEpochResultDto);

                if (div > 0)
                {
                    var offset = timeZone.GetUtcOffset(DateTime.MaxValue);
                    long ticks = DateTime.MaxValue.Ticks;
                    if (offset < TimeSpan.Zero)
                        ticks += offset.Ticks;

                    var maxValue = new DateTimeOffset(ticks, offset).DateTime;
                    parameter.Value = maxValue;
                    var result = await cmd.ExecuteScalarAsync<DateTime>();

                    var expectedValue = new DateTime(maxValue.Ticks / div * div);
                    Assert.Equal(expectedValue, result);
                }
                else
                {
                    DateTimeOffset maxValueDto;
                    if (div == -100)
                        maxValueDto = DateTimeOffset.ParseExact("2554-07-21T23:34:33.7095516Z", "O", CultureInfo.InvariantCulture);
                    else
                        maxValueDto = DateTimeOffset.ParseExact("7815-07-17T19:45:37.0955161Z", "O", CultureInfo.InvariantCulture);

                    var offset = timeZone.GetUtcOffset(maxValueDto);
                    var maxValue = new DateTime(maxValueDto.Ticks + offset.Ticks);

                    parameter.Value = maxValue;
                    var result = await cmd.ExecuteScalarAsync<DateTime>();
                    Assert.Equal(maxValue, result);

                    parameter.Value = maxValue.AddTicks(1);
                    var exception = await Assert.ThrowsAsync<ClickHouseHandledException>(() => cmd.ExecuteScalarAsync());
                    Assert.IsAssignableFrom<OverflowException>(exception.InnerException);
                }
            }
        }

        [Fact]
        public async Task ReadDateTimeParameterWithTimezoneScalar()
        {
            var valueShort = new DateTime(2014, 7, 5, 12, 13, 14);
            var value = valueShort.Add(TimeSpan.FromMilliseconds(123.4567));

            const string targetTzCode = "Asia/Magadan";
            var targetTz = TZConvert.GetTimeZoneInfo(targetTzCode);
            
            await using var connection = await OpenConnectionAsync();
            await using var cmd = connection.CreateCommand($"SELECT toTimeZone({{d}}, '{targetTzCode}')");
            var parameter = new ClickHouseParameter("d") {Value = value, TimeZone = TZConvert.GetTimeZoneInfo("Pacific/Niue"), Precision = 4};
            cmd.Parameters.Add(parameter);
            var deltaOffset = targetTz.GetUtcOffset(valueShort) - parameter.TimeZone.GetUtcOffset(valueShort);

            object resultObj;
            DateTimeOffset result;
            foreach (var parameterType in new ClickHouseDbType?[] {null, ClickHouseDbType.DateTime, ClickHouseDbType.DateTimeOffset})
            {
                if (parameterType != null)
                    parameter.ClickHouseDbType = parameterType.Value;

                resultObj = await cmd.ExecuteScalarAsync();
                result = Assert.IsType<DateTimeOffset>(resultObj);
                Assert.Equal(valueShort + deltaOffset, result.DateTime);
                Assert.Equal(targetTz.GetUtcOffset(result), result.Offset);
            }

            parameter.ClickHouseDbType = ClickHouseDbType.DateTime2;
            resultObj = await cmd.ExecuteScalarAsync();
            result = Assert.IsType<DateTimeOffset>(resultObj);
            Assert.Equal(value + deltaOffset, result.DateTime);
            Assert.Equal(targetTz.GetUtcOffset(result), result.Offset);

            parameter.ClickHouseDbType = ClickHouseDbType.DateTime64;
            resultObj = await cmd.ExecuteScalarAsync();
            result = Assert.IsType<DateTimeOffset>(resultObj);
            Assert.Equal(valueShort + deltaOffset + TimeSpan.FromMilliseconds(123.4), result.DateTime);
            Assert.Equal(targetTz.GetUtcOffset(result), result.Offset);

            parameter.ResetDbType();
            resultObj = await cmd.ExecuteScalarAsync();
            result = Assert.IsType<DateTimeOffset>(resultObj);
            deltaOffset = targetTz.GetUtcOffset(valueShort) - connection.GetServerTimeZone().GetUtcOffset(valueShort);
            Assert.Equal(valueShort + deltaOffset, result.DateTime);
        }

        [Fact]
        public async Task ReadFloatScalar()
        {
            await using var connection = await OpenConnectionAsync();

            var expectedValue = 1234567890.125f;
            await using var cmd = connection.CreateCommand($"SELECT CAST('{expectedValue:#.#}' AS Float32)");

            var result = await cmd.ExecuteScalarAsync();
            var resultFloat = Assert.IsType<float>(result);

            Assert.Equal(expectedValue, resultFloat);
        }

        [Fact]
        public async Task ReadDoubleScalar()
        {
            await using var connection = await OpenConnectionAsync();

            await using var cmd = connection.CreateCommand("SELECT -123456789109876.125");

            var result = await cmd.ExecuteScalarAsync();
            var resultDouble = Assert.IsType<double>(result);

            Assert.Equal(-123456789109876.125, resultDouble);
        }

        [Fact]
        public async Task ReadNothingScalar()
        {
            await using var connection = await OpenConnectionAsync();

            await using var cmd = connection.CreateCommand("SELECT NULL");

            var result = await cmd.ExecuteScalarAsync();
            Assert.IsType<DBNull>(result);
        }

        [Fact]
        public async Task ReadEmptyArrayScalar()
        {
            await using var connection = await OpenConnectionAsync();

            await using var cmd = connection.CreateCommand("SELECT []");

            var result = await cmd.ExecuteScalarAsync();
            var objResult = Assert.IsType<object[]>(result);
            Assert.Empty(objResult);
        }

        [Fact]
        public async Task ReadByteArrayScalar()
        {
            await using var connection = await OpenConnectionAsync();

            await using var cmd = connection.CreateCommand("SELECT [4, 8, 15, 16, 23, 42]");

            var result = await cmd.ExecuteScalarAsync<byte[]>();

            Assert.Equal(new byte[] {4, 8, 15, 16, 23, 42}, result);
        }

        [Fact]
        public async Task ReadNullableByteArrayScalar()
        {
            await using var connection = await OpenConnectionAsync();

            await using var cmd = connection.CreateCommand("SELECT [4, NULL, 8, NULL, 15, NULL, 16, NULL, 23, NULL, 42]");

            var result = await cmd.ExecuteScalarAsync();
            var resultArr = Assert.IsType<byte?[]>(result);

            Assert.Equal(new byte?[] { 4, null, 8, null, 15, null, 16, null, 23, null, 42 }, resultArr);
        }

        [Fact]
        public async Task ReadArrayOfArraysOfArraysScalar()
        {
            const string query = @"SELECT 
                                        [
                                            [
                                                [1],
                                                [],
                                                [2, NULL]
                                            ],
                                            [
                                                [3]
                                            ]
                                        ]";

            await using var connection = await OpenConnectionAsync();

            await using var cmd = connection.CreateCommand(query);

            var result = await cmd.ExecuteScalarAsync();
            var resultArr = Assert.IsType<byte?[][][]>(result);

            Assert.NotNull(resultArr);
            Assert.Equal(2, resultArr.Length);

            Assert.NotNull(resultArr[0]);
            Assert.Equal(3, resultArr[0].Length);

            Assert.Equal(new byte?[] {1}, resultArr[0][0]);
            Assert.Equal(new byte?[0], resultArr[0][1]);
            Assert.Equal(new byte?[] {2, null}, resultArr[0][2]);

            Assert.NotNull(resultArr[1]);
            Assert.Equal(1, resultArr[1].Length);

            Assert.Equal(new byte?[] {3}, resultArr[1][0]);
        }

        [Fact]
        public async Task ReadNullableByteArrayAsUInt64ArrayScalar()
        {
            await using var connection = await OpenConnectionAsync();

            await using var cmd = connection.CreateCommand("SELECT [4, NULL, 8, NULL, 15, NULL, 16, NULL, 23, NULL, 42]");

            var result = await cmd.ExecuteScalarAsync<ulong?[]>();

            Assert.Equal(new ulong?[] { 4, null, 8, null, 15, null, 16, null, 23, null, 42 }, result);
        }

        [Fact]
        public async Task ReadNullableStringArrayScalar()
        {
            await using var connection = await OpenConnectionAsync();

            await using var cmd = connection.CreateCommand("SELECT ['All', NULL, 'your', NULL, 'base', NULL, 'are', NULL, 'belong', NULL, 'to', NULL, 'us!']");

            var result = await cmd.ExecuteScalarAsync<string?[]>();

            Assert.Equal(new[] {"All", null, "your", null, "base", null, "are", null, "belong", null, "to", null, "us!"}, result);
        }

        [Fact]
        public async Task ReadNullableNothingArrayScalar()
        {
            await using var connection = await OpenConnectionAsync();

            await using var cmd = connection.CreateCommand("SELECT [NULL,NULL,NULL]");

            var result = await cmd.ExecuteScalarAsync();
            var resultArr = Assert.IsType<object?[]>(result);

            Assert.Equal(new object[3], resultArr);
        }

        [Fact]
        public async Task ReadArrayParameterScalar()
        {
            await using var connection = await OpenConnectionAsync();

            await using var cmd = connection.CreateCommand("SELECT {p}");
            var expectedResult = new[] {4, 8, 15, 16, 23, 42};
            var param = cmd.Parameters.AddWithValue("p", expectedResult);

            var result = await cmd.ExecuteScalarAsync();
            var intResult = Assert.IsType<int[]>(result);

            Assert.Equal(expectedResult, intResult);

            param.IsArray = true;
            param.DbType = DbType.Decimal;

            result = await cmd.ExecuteScalarAsync();
            var decResult = Assert.IsType<decimal[]>(result);

            Assert.Equal(expectedResult.Select(v => (decimal) v), decResult);
        }

        [Fact]
        public async Task ReadArrayOfArraysParameterScalar()
        {
            await using var connection = await OpenConnectionAsync();

            await using var cmd = connection.CreateCommand("SELECT {p}");
            var expectedResult = new List<uint[]> {new uint[] {4, 8}, new uint[] {15, 16, 23}, new uint[] {42}};
            var param = cmd.Parameters.AddWithValue("p", expectedResult);

            var result = await cmd.ExecuteScalarAsync();
            var intResult = Assert.IsType<uint[][]>(result);

            Assert.Equal(expectedResult.Count, intResult.Length);
            for (int i = 0; i < expectedResult.Count; i++)
                Assert.Equal(expectedResult[i], intResult[i]);

            param.ArrayRank = 2;
            param.DbType = DbType.UInt64;
            param.IsNullable = true;

            result = await cmd.ExecuteScalarAsync();
            var decResult = Assert.IsType<ulong?[][]>(result);

            Assert.Equal(expectedResult.Count, decResult.Length);
            for (int i = 0; i < decResult.Length; i++)
                Assert.Equal(expectedResult[i].Select(v => (ulong?) v), decResult[i]);
        }

        [Fact]
        public async Task ReadMultidimensionalArrayParameterScalar()
        {
            await using var connection = await OpenConnectionAsync();

            await using var cmd = connection.CreateCommand("SELECT {p}");
            var expectedResult = new List<int?[,]> {new[,] {{(int?) 4, 8}, {15, 16}, {23, 42}}, new[,] {{1}, {(int?) null}, {3}}, new[,] {{(int?) -4, -8, -15}, {-16, -23, -42}}};
            var param = cmd.Parameters.AddWithValue("p", expectedResult);

            var result = await cmd.ExecuteScalarAsync();
            var intResult = Assert.IsType<int?[][][]>(result);

            Assert.Equal(expectedResult.Count, intResult.Length);
            for (int i = 0; i < expectedResult.Count; i++)
            {
                var expectedLengthI = expectedResult[i].GetLength(0);
                var expectedLengthJ = expectedResult[i].GetLength(1);
                Assert.Equal(expectedLengthI, intResult[i].Length);
                for (int j = 0; j < expectedLengthI; j++)
                {
                    Assert.Equal(expectedLengthJ, intResult[i][j].Length);
                    for (int k = 0; k < expectedLengthJ; k++)
                        Assert.Equal(expectedResult[i][j, k], intResult[i][j][k]);
                }
            }

            param.ArrayRank = 3;
            param.DbType = DbType.Decimal;

            result = await cmd.ExecuteScalarAsync();
            var decResult = Assert.IsType<decimal?[][][]>(result);

            Assert.Equal(expectedResult.Count, decResult.Length);
            for (int i = 0; i < expectedResult.Count; i++)
            {
                var expectedLengthI = expectedResult[i].GetLength(0);
                var expectedLengthJ = expectedResult[i].GetLength(1);
                Assert.Equal(expectedLengthI, decResult[i].Length);
                for (int j = 0; j < expectedLengthI; j++)
                {
                    Assert.Equal(expectedLengthJ, decResult[i][j].Length);
                    for (int k = 0; k < expectedLengthJ; k++)
                        Assert.Equal(expectedResult[i][j, k], decResult[i][j][k]);
                }
            }
        }

        [Fact]
        public async Task ReadEnumColumn()
        {
            await using var connection = await OpenConnectionAsync();

            await using var cmd = connection.CreateCommand(@"SELECT CAST(T.col AS Enum(''=0, '\\e\s\c\\a\p\\e'=1, '\'val\''=2, '\r\n\t\d\\\r\n'=3,'\a\b\c\d\e\f\g\h\i\j\k\l\m\n\o\p\q\r\s\t\u\v\w\x20\y\z'=4)) AS enumVal, toString(enumVal) AS strVal
    FROM (SELECT 0 AS col UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4) AS T");

            await using var reader = await cmd.ExecuteReaderAsync();
            var columnType = reader.GetFieldTypeInfo(0);
            var typeNames = new Dictionary<int, string>();
            for(int i=0; i<columnType.TypeArgumentsCount; i++)
            {
                var obj = columnType.GetTypeArgument(i);
                var pair = Assert.IsType<KeyValuePair<string, sbyte>>(obj);
                typeNames.Add(pair.Value, pair.Key);
            }

            int bitmap = 0;
            while (await reader.ReadAsync())
            {
                var value = reader.GetFieldValue<int>(0);
                var defaultValue = reader.GetValue(0);
                var strValue = Assert.IsType<string>(defaultValue);
                var expectedStrValue = reader.GetFieldValue<string>(1);
                
                Assert.Equal(expectedStrValue, strValue);

                switch (value)
                {
                    case 0:
                        Assert.Equal(string.Empty, strValue);
                        break;
                    case 1:
                        Assert.Equal(@"\e\s\c\a\p\e", strValue);
                        break;
                    case 2:
                        Assert.Equal("'val'", strValue);
                        break;
                    case 3:
                        Assert.Equal("\r\n\t\\d\\\r\n", strValue);
                        break;
                    case 4:
                        Assert.Equal("\a\b\\c\\d\u001b\f\\g\\h\\i\\j\\k\\l\\m\n\\o\\p\\q\r\\s\t\\u\v\\w\x20\\y\\z", strValue);
                        break;
                    default:
                        Assert.True(false, $"Unexpected value: {value}.");
                        break;
                }

                Assert.Equal(strValue, typeNames[value]);

                bitmap ^= 1 << value;
            }

            Assert.Equal(31, bitmap);
        }

        [Fact]
        public async Task ReadEnumScalar()
        {
            await using var connection = await OpenConnectionAsync();

            await using var cmd = connection.CreateCommand("SELECT CAST(42 AS Enum('' = 0, 'b' = -129, 'Hello, world! :)' = 42))");

            var result = await cmd.ExecuteScalarAsync();
            Assert.Equal("Hello, world! :)", result);
        }

        [Fact]
        public async Task ReadClrEnumScalar()
        {
            await using var connection = await OpenConnectionAsync();

            await using var cmd = connection.CreateCommand("SELECT CAST(42 AS Enum('' = 0, 'b' = -129, 'Hello, world! :)' = 42))");

            var settings = new ClickHouseColumnSettings(new ClickHouseEnumConverter<TestEnum>());
            var result = await cmd.ExecuteScalarAsync(settings);
            Assert.Equal(TestEnum.Value1, result);
        }

        [Fact]
        public async Task ReadInt32ArrayColumn()
        {
            int?[]?[] expected = new int?[10][];

            var queryBuilder = new StringBuilder("SELECT T.* FROM (").AppendLine();
            for (int i = 0, k = 0; i < expected.Length; i++)
            {
                if (i > 0)
                    queryBuilder.AppendLine().Append("UNION ALL ");

                queryBuilder.Append("SELECT ").Append(i).Append(" AS num, [");
                var expectedArray = expected[i] = new int?[i + 1];
                for (int j = 0; j < expectedArray.Length; j++, k++)
                {
                    if (j > 0)
                        queryBuilder.Append(", ");

                    if ((k % 3 == 0) == (k % 5 == 0))
                    {
                        queryBuilder.Append("CAST(").Append(k).Append(" AS Nullable(Int32))");
                        expectedArray[j] = k;
                    }
                    else
                    {
                        queryBuilder.Append("CAST(NULL AS Nullable(Int32))");
                    }
                }

                queryBuilder.Append("] AS arr");
            }

            var queryString = queryBuilder.AppendLine(") AS T").Append("ORDER BY T.num DESC").ToString();

            await using var connection = await OpenConnectionAsync();

            var cmd = connection.CreateCommand(queryString);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var num = reader.GetInt32(0);

                var expectedArray = expected[num];
                Assert.NotNull(expectedArray);

                var value = reader.GetValue(1);
                var array = Assert.IsType<int?[]>(value);

                Assert.Equal(expectedArray, array);

                expected[num] = null;
            }

            Assert.All(expected, Assert.Null);
        }

        [Fact]
        public async Task SkipInt32ArrayColumn()
        {
            int?[]?[] expected = new int?[10][];

            var queryBuilder = new StringBuilder("SELECT T.* FROM (").AppendLine();
            for (int i = 0, k = 0; i < expected.Length; i++)
            {
                if (i > 0)
                    queryBuilder.AppendLine().Append("UNION ALL ");

                queryBuilder.Append("SELECT ").Append(i).Append(" AS num, [");
                var expectedArray = expected[i] = new int?[i + 1];
                for (int j = 0; j < expectedArray.Length; j++, k++)
                {
                    if (j > 0)
                        queryBuilder.Append(", ");

                    if ((k % 3 == 0) == (k % 5 == 0))
                    {
                        queryBuilder.Append("CAST(").Append(k).Append(" AS Nullable(Int32))");
                        expectedArray[j] = k;
                    }
                    else
                    {
                        queryBuilder.Append("CAST(NULL AS Nullable(Int32))");
                    }
                }

                queryBuilder.Append("] AS arr");
            }

            var queryString = queryBuilder.AppendLine(") AS T").Append("ORDER BY T.num DESC").ToString();

            await using var connection = await OpenConnectionAsync();

            var cmd = connection.CreateCommand(queryString);
            await cmd.ExecuteNonQueryAsync();
        }

        [Fact]
        public async Task ReadTuplesWithDifferentLength()
        {
            var tupleSb = new StringBuilder("tuple(");
            var querySb = new StringBuilder("SELECT").AppendLine();

            for (int i = 0; i < 15; i++)
            {
                if (i > 0)
                {
                    tupleSb.Append(", ");
                    querySb.AppendLine(", ");
                }

                tupleSb.Append("CAST(").Append(i + 1).Append(" AS Int32)");
                querySb.Append(tupleSb, 0, tupleSb.Length).Append($") AS t{i}");
            }

            var queryString = querySb.ToString();

            await using var connection = await OpenConnectionAsync();

            var cmd = connection.CreateCommand(queryString);
            await using var reader = await cmd.ExecuteReaderAsync();

            var success = await reader.ReadAsync();
            Assert.True(success);

            var expected = new object[]
            {
                Tuple.Create(1),
                Tuple.Create(1, 2),
                Tuple.Create(1, 2, 3),
                Tuple.Create(1, 2, 3, 4),
                Tuple.Create(1, 2, 3, 4, 5),
                Tuple.Create(1, 2, 3, 4, 5, 6),
                Tuple.Create(1, 2, 3, 4, 5, 6, 7),
                Tuple.Create(1, 2, 3, 4, 5, 6, 7, 8),
                new Tuple<int, int, int, int, int, int, int, Tuple<int, int>>(1, 2, 3, 4, 5, 6, 7, Tuple.Create(8, 9)),
                new Tuple<int, int, int, int, int, int, int, Tuple<int, int, int>>(1, 2, 3, 4, 5, 6, 7, Tuple.Create(8, 9, 10)),
                new Tuple<int, int, int, int, int, int, int, Tuple<int, int, int, int>>(1, 2, 3, 4, 5, 6, 7, Tuple.Create(8, 9, 10, 11)),
                new Tuple<int, int, int, int, int, int, int, Tuple<int, int, int, int, int>>(1, 2, 3, 4, 5, 6, 7, Tuple.Create(8, 9, 10, 11, 12)),
                new Tuple<int, int, int, int, int, int, int, Tuple<int, int, int, int, int, int>>(1, 2, 3, 4, 5, 6, 7, Tuple.Create(8, 9, 10, 11, 12, 13)),
                new Tuple<int, int, int, int, int, int, int, Tuple<int, int, int, int, int, int, int>>(1, 2, 3, 4, 5, 6, 7, Tuple.Create(8, 9, 10, 11, 12, 13, 14)),
                new Tuple<int, int, int, int, int, int, int, Tuple<int, int, int, int, int, int, int, Tuple<int>>>(1, 2, 3, 4, 5, 6, 7, Tuple.Create(8, 9, 10, 11, 12, 13, 14, 15))
            };

            for (int i = 0; i < 15; i++)
            {
                var columnType = reader.GetFieldType(i);
                Assert.Equal(expected[i].GetType(), columnType);

                var columnValue = reader.GetValue(i);

                if (i == 12)
                {
                    var reinterpretedValue = reader.GetFieldValue<Tuple<int?, long, long?, int, int?, int, int?, Tuple<long, long?, int, int?, int, int?>>>(i);
                    var expectedValue = new Tuple<int?, long, long?, int, int?, int, int?, Tuple<long, long?, int, int?, int, int?>>(
                        1,
                        2,
                        3,
                        4,
                        5,
                        6,
                        7,
                        Tuple.Create((long) 8, (long?) 9, 10, (int?) 11, 12, (int?) 13));

                    Assert.Equal(expectedValue, reinterpretedValue);
                }

                Assert.Equal(expected[i].GetType(), columnValue.GetType());
                Assert.Equal(columnValue, expected[i]);
            }

            Assert.False(await reader.ReadAsync());
        }

        [Fact]
        public async Task ReadValueTuplesWithDifferentLength()
        {
            var tupleSb = new StringBuilder("tuple(");
            var querySb = new StringBuilder("SELECT").AppendLine();

            for (int i = 0; i < 15; i++)
            {
                if (i > 0)
                {
                    tupleSb.Append(", ");
                    querySb.AppendLine(", ");
                }

                tupleSb.Append("CAST(").Append(i + 1).Append(" AS Int32)");
                querySb.Append(tupleSb, 0, tupleSb.Length).Append($") AS t{i}");
            }

            var queryString = querySb.ToString();

            await using var connection = await OpenConnectionAsync();

            var cmd = connection.CreateCommand(queryString);
            await using var reader = await cmd.ExecuteReaderAsync();

            var success = await reader.ReadAsync();
            Assert.True(success);
            Assert.Equal(15, reader.FieldCount);

            for (int i = 0; i < reader.FieldCount; i++)
            {
                switch (i)
                {
                    case 0:
                        AssertEqual(reader, i, new ValueTuple<int>(1));
                        break;
                    case 1:
                        AssertEqual(reader, i, (1, (int?) 2));
                        break;
                    case 2:
                        AssertEqual(reader, i, (1, 2, 3));
                        break;
                    case 3:
                        AssertEqual(reader, i, (1, 2, 3, 4));
                        break;
                    case 4:
                        AssertEqual(reader, i, (1, 2, 3, 4, 5));
                        break;
                    case 5:
                        AssertEqual(reader, i, (1, 2, 3, 4, 5, 6));
                        break;
                    case 6:
                        AssertEqual(reader, i, (1, 2, 3, 4, 5, 6, 7));
                        break;
                    case 7:
                        AssertEqual(reader, i, (1, 2, 3, 4, 5, 6, 7, (long) 8));
                        break;
                    case 8:
                        AssertEqual(reader, i, (1, 2, 3, 4, (int?) 5, 6, 7, (long?) 8, 9));
                        break;
                    case 9:
                        AssertEqual(reader, i, (1, 2, 3, 4, 5, 6, 7, 8, 9, 10));
                        break;
                    case 10:
                        AssertEqual(reader, i, (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11));
                        break;
                    case 11:
                        AssertEqual(reader, i, (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12));
                        break;
                    case 12:
                        AssertEqual<(int?, long, long?, int, int?, int, int?, long, long?, int, int?, int, int?)>(reader, i, (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13));
                        break;
                    case 13:
                        AssertEqual(reader, i, (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14));
                        break;
                    case 14:
                        AssertEqual(reader, i, (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, (long?) 15));
                        break;
                    default:
                        Assert.True(i >= 0 && i < 15, "Too many columns.");
                        break;
                }
            }

            Assert.False(await reader.ReadAsync());

            static void AssertEqual<T>(DbDataReader reader, int ordinal, T expectedValue)
            {
                var value = reader.GetFieldValue<T>(ordinal);
                Assert.Equal(expectedValue, value);
            }
        }

        [Fact]
        public async Task ReadTupleColumn()
        {
            const string query = @"SELECT T.tval
    FROM (
        SELECT tuple(cast(1 as Decimal(13, 4)), cast('one' as Nullable(String)), cast('1999-09-09 09:09:09' as Nullable(DateTime))) AS tval
        UNION ALL SELECT tuple(2, 'two', cast('2019-12-11 16:55:54' as DateTime('Asia/Yekaterinburg')))
        UNION ALL SELECT tuple(3, null, cast('2007-01-11 05:32:48' as DateTime))) T
    ORDER BY T.tval.3";

            await using var connection = await OpenConnectionAsync();

            var cmd = connection.CreateCommand(query);
            await using var reader = await cmd.ExecuteReaderAsync();

            int count = 0;
            while (await reader.ReadAsync())
            {
                var value = reader.GetFieldValue<Tuple<decimal, string?, DateTime?>>(0);
                DateTime dt;
                switch (count)
                {
                    case 0:
                        Assert.Equal(1, value.Item1);
                        Assert.Equal("one", value.Item2);
                        dt = new DateTime(1999, 9, 9, 9, 9, 9);
                        Assert.Equal(dt, value.Item3);
                        break;

                    case 1:
                        Assert.Equal(3, value.Item1);
                        Assert.Null(value.Item2);
                        dt = new DateTime(2007, 1, 11, 5, 32, 48);
                        Assert.Equal(dt, value.Item3);
                        break;

                    case 2:
                        Assert.Equal(2, value.Item1);
                        Assert.Equal("two", value.Item2);
                        var tz = TZConvert.GetTimeZoneInfo("Asia/Yekaterinburg");
                        dt = TimeZoneInfo.ConvertTime(new DateTime(2019, 12, 11, 16, 55, 54), tz, connection.GetServerTimeZone());
                        Assert.Equal(dt, value.Item3);
                        break;

                    default:
                        Assert.False(true, "Too many rows.");
                        break;
                }

                ++count;
            }

            Assert.Equal(3, count);
        }

        [Fact]
        public async Task ReadValueTupleColumn()
        {
            const string query = @"SELECT T.tval
    FROM (
        SELECT tuple(cast(1 as Decimal(13, 4)), cast('one' as Nullable(String)), cast('1999-09-09 09:09:09' as Nullable(DateTime))) AS tval
        UNION ALL SELECT tuple(2, 'two', cast('2019-12-11 16:55:54' as DateTime('Asia/Yekaterinburg')))
        UNION ALL SELECT tuple(3, null, cast('2007-01-11 05:32:48' as DateTime))) T
    ORDER BY T.tval.3";

            await using var connection = await OpenConnectionAsync();

            var cmd = connection.CreateCommand(query);
            await using var reader = await cmd.ExecuteReaderAsync();

            int count = 0;
            while (await reader.ReadAsync())
            {
                var value = reader.GetFieldValue<(decimal number, string? str, DateTime? date)>(0);
                DateTime dt;
                switch (count)
                {
                    case 0:
                        Assert.Equal(1, value.number);
                        Assert.Equal("one", value.str);
                        dt = new DateTime(1999, 9, 9, 9, 9, 9);
                        Assert.Equal(dt, value.date);
                        break;

                    case 1:
                        Assert.Equal(3, value.number);
                        Assert.Null(value.str);
                        dt = new DateTime(2007, 1, 11, 5, 32, 48);
                        Assert.Equal(dt, value.date);
                        break;

                    case 2:
                        Assert.Equal(2, value.number);
                        Assert.Equal("two", value.str);
                        dt = new DateTime(2019, 12, 11, 16, 55, 54);
                        dt = TimeZoneInfo.ConvertTime(dt, TZConvert.GetTimeZoneInfo("Asia/Yekaterinburg"), connection.GetServerTimeZone());
                        Assert.Equal(dt, value.date);
                        break;

                    default:
                        Assert.False(true, "Too many rows.");
                        break;
                }

                ++count;
            }

            Assert.Equal(3, count);
        }

        [Fact]
        public async Task ReadNamedTupleScalar()
        {
            await using var connection = await OpenConnectionAsync();
            var cmd = connection.CreateCommand("SELECT CAST(('hello', 1, -1) AS Tuple(name String, id UInt32, `e \\`e\\` e` Int32))");
            var result = await cmd.ExecuteScalarAsync<(string firstItem, uint secondItem, int thirdItem)>();
            Assert.Equal(("hello", 1u, -1), result);
        }

        [Fact]
        public async Task SkipTupleColumn()
        {
            const string query = @"SELECT T.tval
    FROM (
        SELECT tuple(cast(1 as Decimal(13, 4)), cast('one' as Nullable(String)), cast('1999-09-09 09:09:09' as Nullable(DateTime))) AS tval
        UNION ALL SELECT tuple(2, 'two', cast('2019-12-11 16:55:54' as DateTime('Asia/Yekaterinburg')))
        UNION ALL SELECT tuple(3, null, cast('2007-01-11 05:32:48' as DateTime))) T
    ORDER BY T.tval.3";

            await using var connection = await OpenConnectionAsync();

            var cmd = connection.CreateCommand(query);
            await cmd.ExecuteNonQueryAsync();
        }

        [Fact]
        public async Task ReadIpV4Column()
        {
            try
            {
                await using var connection = await OpenConnectionAsync();

                var cmd = connection.CreateCommand("DROP TABLE IF EXISTS ip4_test");
                await cmd.ExecuteNonQueryAsync();

                cmd = connection.CreateCommand("CREATE TABLE ip4_test(val IPv4, strVal String) ENGINE=Memory");
                await cmd.ExecuteNonQueryAsync();

                cmd.CommandText = "INSERT INTO ip4_test(val, strVal) VALUES ('116.253.40.133','116.253.40.133')('10.0.151.56','10.0.151.56')('192.0.121.234','192.0.121.234')";
                await cmd.ExecuteNonQueryAsync();

                cmd.CommandText = "SELECT val, strVal FROM ip4_test";
                int count = 0;
                await using (var reader = cmd.ExecuteReader())
                {
                    while (await reader.ReadAsync())
                    {
                        var ipAddr = reader.GetFieldValue<IPAddress>(0);
                        var ipAddrStr = reader.GetFieldValue<string>(1);
                        var expectedIpAddr = IPAddress.Parse(ipAddrStr);

                        Assert.Equal(expectedIpAddr, ipAddr);
                        ++count;
                    }
                }

                Assert.Equal(3, count);
            }
            finally
            {
                await using var connection = await OpenConnectionAsync();
                var cmd = connection.CreateCommand("DROP TABLE IF EXISTS ip4_test");
                await cmd.ExecuteNonQueryAsync();
            }
        }

        [Fact]
        public async Task ReadIpV6Column()
        {
            try
            {
                await using var connection = await OpenConnectionAsync();

                var cmd = connection.CreateCommand("DROP TABLE IF EXISTS ip6_test");
                await cmd.ExecuteNonQueryAsync();

                cmd = connection.CreateCommand("CREATE TABLE ip6_test(val IPv6, strVal String) ENGINE=Memory");
                await cmd.ExecuteNonQueryAsync();

                cmd.CommandText = "INSERT INTO ip6_test(val, strVal) VALUES ('2001:0db8:11a3:09d7:1f34:8a2e:07a0:765d','2001:0db8:11a3:09d7:1f34:8a2e:07a0:765d')('2a02:aa08:e000:3100::2','2a02:aa08:e000:3100::2')('::ffff:192.0.121.234','::ffff:192.0.121.234')";
                await cmd.ExecuteNonQueryAsync();

                cmd.CommandText = "SELECT val, strVal FROM ip6_test";
                int count = 0;
                await using (var reader = cmd.ExecuteReader())
                {
                    while (await reader.ReadAsync())
                    {
                        var ipAddr = reader.GetFieldValue<IPAddress>(0);
                        var ipAddrStr = reader.GetFieldValue<string>(1);
                        var expectedIpAddr = IPAddress.Parse(ipAddrStr);

                        Assert.Equal(expectedIpAddr, ipAddr);
                        ++count;
                    }
                }

                Assert.Equal(3, count);
            }
            finally
            {
                await using var connection = await OpenConnectionAsync();
                var cmd = connection.CreateCommand("DROP TABLE IF EXISTS ip6_test");
                await cmd.ExecuteNonQueryAsync();
            }
        }

        [Fact]
        public async Task ReadLowCardinalityColumn()
        {
            try
            {
                await using var connection = await OpenConnectionAsync();

                var cmd = connection.CreateCommand("DROP TABLE IF EXISTS low_cardinality_test");
                await cmd.ExecuteNonQueryAsync();

                cmd = connection.CreateCommand("CREATE TABLE low_cardinality_test(id Int32, str LowCardinality(String)) ENGINE=Memory");
                await cmd.ExecuteNonQueryAsync();

                cmd.CommandText = "INSERT INTO low_cardinality_test(id, str) VALUES (1,'foo')(2,'bar')(4,'bar')(6,'bar')(3,'foo')(7,'foo')(8,'bar')(5,'foobar')";
                await cmd.ExecuteNonQueryAsync();

                cmd.CommandText = "SELECT id, str FROM low_cardinality_test";
                int count = 0;
                await using (var reader = cmd.ExecuteReader())
                {
                    while (await reader.ReadAsync())
                    {
                        var id = reader.GetInt32(0);
                        var str = reader.GetString(1);

                        var expected = id == 5 ? "foobar" : id % 2 == 1 ? "foo" : "bar";
                        Assert.Equal(expected, str);
                        ++count;
                    }
                }

                Assert.Equal(8, count);
            }
            finally
            {
                await using var connection = await OpenConnectionAsync();
                var cmd = connection.CreateCommand("DROP TABLE IF EXISTS low_cardinality_test");
                await cmd.ExecuteNonQueryAsync();
            }
        }

        [Fact]
        public async Task ReadNullableLowCardinalityColumn()
        {
            try
            {
                await using var connection = await OpenConnectionAsync();

                var cmd = connection.CreateCommand("DROP TABLE IF EXISTS low_cardinality_null_test");
                await cmd.ExecuteNonQueryAsync();

                cmd = connection.CreateCommand("CREATE TABLE low_cardinality_null_test(id Int32, str LowCardinality(Nullable(String))) ENGINE=Memory");
                await cmd.ExecuteNonQueryAsync();

                cmd.CommandText = "INSERT INTO low_cardinality_null_test(id, str) SELECT number, number%50 == 0 ? NULL : toString(number%200) FROM system.numbers LIMIT 30000";
                await cmd.ExecuteNonQueryAsync();

                cmd.CommandText = "INSERT INTO low_cardinality_null_test(id, str) SELECT number, number%50 == 0 ? NULL : toString(number%200) FROM system.numbers WHERE number>=30000 LIMIT 30000";
                await cmd.ExecuteNonQueryAsync();

                cmd.CommandText = "INSERT INTO low_cardinality_null_test(id, str) SELECT number, number%50 == 0 ? NULL : toString(number%400) FROM system.numbers WHERE number>=60000 LIMIT 30000";
                await cmd.ExecuteNonQueryAsync();

                cmd.CommandText = "SELECT id, str FROM low_cardinality_null_test";
                int count = 0;
                await using (var reader = cmd.ExecuteReader())
                {
                    while (await reader.ReadAsync())
                    {
                        var id = reader.GetInt32(0);
                        var str = reader.GetString(1, null);

                        if (id % 50 == 0)
                            Assert.Null(str);
                        else if (id < 60000)
                            Assert.Equal((id % 200).ToString(), str);
                        else
                            Assert.Equal((id % 400).ToString(), str);

                        ++count;
                    }
                }

                Assert.Equal(90000, count);
            }
            finally
            {
                await using var connection = await OpenConnectionAsync();
                var cmd = connection.CreateCommand("DROP TABLE IF EXISTS low_cardinality_null_test");
                await cmd.ExecuteNonQueryAsync();
            }
        }

        [Fact]
        public async Task SkipLowCardinalityColumn()
        {
            try
            {
                await using var connection = await OpenConnectionAsync();

                var cmd = connection.CreateCommand("DROP TABLE IF EXISTS low_cardinality_skip_test");
                await cmd.ExecuteNonQueryAsync();

                cmd = connection.CreateCommand("CREATE TABLE low_cardinality_skip_test(id Int32, str LowCardinality(Nullable(String))) ENGINE=Memory");
                await cmd.ExecuteNonQueryAsync();

                cmd.CommandText = "INSERT INTO low_cardinality_skip_test(id, str) SELECT number, number%50 == 0 ? NULL : toString(number%200) FROM system.numbers LIMIT 10000";
                await cmd.ExecuteNonQueryAsync();

                cmd.CommandText = "INSERT INTO low_cardinality_skip_test(id, str) SELECT number, number%50 == 0 ? NULL : toString(number%200) FROM system.numbers WHERE number>=10000 LIMIT 10000";
                await cmd.ExecuteNonQueryAsync();

                cmd.CommandText = "SELECT id, str FROM low_cardinality_skip_test";
                int count = 0;
                await using (var reader = cmd.ExecuteReader())
                {
                    while (await reader.ReadAsync())
                    {
                        var id = reader.GetInt32(0);
                        var str = reader.GetString(1, null);

                        if (id % 50 == 0)
                            Assert.Null(str);
                        else
                            Assert.Equal((id % 200).ToString(), str);

                        if (++count == 100)
                            break;
                    }
                }

                Assert.Equal(100, count);

                cmd.CommandText = "SELECT count(*) FROM low_cardinality_skip_test";
                count = (int) await cmd.ExecuteScalarAsync<ulong>();
                Assert.Equal(20000, count);
            }
            finally
            {
                await using var connection = await OpenConnectionAsync();
                var cmd = connection.CreateCommand("DROP TABLE IF EXISTS low_cardinality_skip_test");
                await cmd.ExecuteNonQueryAsync();
            }
        }

        [Fact]
        public async Task ReadFixedStringParameterScalar()
        {
            var values = new[] {string.Empty, "0", "12345678", "abcdefg", "1234", "abcd", "абвг"};

            await using var connection = await OpenConnectionAsync();

            await using var cmd = connection.CreateCommand("SELECT {param}");
            var param = new ClickHouseParameter("param") {DbType = DbType.StringFixedLength, Size = 8};
            cmd.Parameters.Add(param);
            
            foreach (var testValue in values)
            {
                param.Value = testValue;

                var value = await cmd.ExecuteScalarAsync<byte[]>();
                var len = value.Length - value.Reverse().TakeWhile(b => b == 0).Count();
                var strValue = Encoding.UTF8.GetString(value, 0, len);
                Assert.Equal(testValue, strValue);
            }

            param.Value = "123456789";
            var exception = await Assert.ThrowsAnyAsync<ClickHouseException>(() => cmd.ExecuteScalarAsync<byte[]>());
            Assert.Equal(exception.ErrorCode, ClickHouseErrorCodes.InvalidQueryParameterConfiguration);

            param.Value = "абвг0";
            exception = await Assert.ThrowsAnyAsync<ClickHouseException>(() => cmd.ExecuteScalarAsync<byte[]>());
            Assert.Equal(exception.ErrorCode, ClickHouseErrorCodes.InvalidQueryParameterConfiguration);
        }

        [Fact]
        public async Task ReadGuidParameterScalar()
        {
            var parameterValue = Guid.Parse("7FCFFE2D-E9A6-49E0-B8ED-9617603F5584");

            await using var connection = await OpenConnectionAsync();

            await using var cmd = connection.CreateCommand("SELECT {param}");
            var param = new ClickHouseParameter("param") { DbType = DbType.Guid };
            cmd.Parameters.Add(param);
            param.Value = parameterValue;

            // ClickHouse bug https://github.com/ClickHouse/ClickHouse/issues/7834
            var ex = await Assert.ThrowsAsync<ClickHouseServerException>(() => cmd.ExecuteScalarAsync<Guid>());
            Assert.StartsWith("DB::Exception: There are no UInt128 literals in SQL", ex.Message);
            
            //var result = await cmd.ExecuteScalarAsync<Guid>();
            //Assert.Equal(parameterValue, result);
        }

        [Fact]
        public async Task ReadDecimalParameterScalar()
        {
            // The default ClickHouse type for decimal is Decimal128(9)
            const decimal minValueByDefault = 1m / 1_000_000_000;
            const decimal binarySparseValue = 281479271677952m * 4294967296m;

            var testData = new[]
            {
                decimal.Zero, decimal.One, decimal.MinusOne, decimal.MinValue, decimal.MaxValue, decimal.MinValue / 100, decimal.MaxValue / 100, decimal.One / 100, decimal.MinusOne / 100,
                minValueByDefault, -minValueByDefault, minValueByDefault / 10, -minValueByDefault / 10, binarySparseValue, -binarySparseValue
            };

            await using var connection = await OpenConnectionAsync();

            await using var cmd = connection.CreateCommand("SELECT {param}");
            var param = new ClickHouseParameter("param") {DbType = DbType.Decimal};
            cmd.Parameters.Add(param);
            
            foreach (var testValue in testData)
            {
                param.Value = testValue;

                var result = await cmd.ExecuteScalarAsync();
                var resultDecimal = Assert.IsType<decimal>(result);

                if (Math.Abs(testValue) >= minValueByDefault)
                    Assert.Equal(testValue, resultDecimal);
                else
                    Assert.Equal(0, resultDecimal);
            }
        }

        [Fact]
        public async Task ReadCurrencyParameterScalar()
        {
            const decimal maxCurrencyValue = 922_337_203_685_477.5807m, minCurrencyValue = -922_337_203_685_477.5808m, binarySparseValue = 7_205_759_833_289.5232m, currencyEpsilon = 0.0001m;

            var testData = new[]
            {
                decimal.Zero, decimal.One, decimal.MinusOne, minCurrencyValue, maxCurrencyValue, decimal.One / 100, decimal.MinusOne / 100,
                binarySparseValue, -binarySparseValue, currencyEpsilon, -currencyEpsilon, currencyEpsilon / 10, -currencyEpsilon / 10
            };

            await using var connection = await OpenConnectionAsync();

            await using var cmd = connection.CreateCommand("SELECT {param}");
            var param = new ClickHouseParameter("param") { DbType = DbType.Currency };
            cmd.Parameters.Add(param);

            param.Value = minCurrencyValue - currencyEpsilon;
            var handledException = await Assert.ThrowsAsync<ClickHouseHandledException>(() => cmd.ExecuteScalarAsync());
            Assert.IsType<OverflowException>(handledException.InnerException);

            param.Value = maxCurrencyValue + currencyEpsilon;
            handledException = await Assert.ThrowsAsync<ClickHouseHandledException>(() => cmd.ExecuteScalarAsync());
            Assert.IsType<OverflowException>(handledException.InnerException);

            foreach (var testValue in testData)
            {
                param.Value = testValue;

                var result = await cmd.ExecuteScalarAsync();
                var resultDecimal = Assert.IsType<decimal>(result);

                if (Math.Abs(testValue) >= currencyEpsilon)
                    Assert.Equal(testValue, resultDecimal);
                else
                    Assert.Equal(0, resultDecimal);
            }
        }

        [Fact]
        public async Task ReadVarNumericParameter()
        {
            await using var connection = await OpenConnectionAsync();

            await using var cmd = connection.CreateCommand("SELECT {param} AS p, toString(p)");
            var param = new ClickHouseParameter("param") {DbType = DbType.VarNumeric, Precision = 7, Scale = 3};
            cmd.Parameters.Add(param);

            {
                const decimal epsilon = 0.001m, formalMax = 9_999.999m, actualMax = 2_147_483.647m, actualMin = -2_147_483.648m;
                var values = new[] {decimal.Zero, decimal.One, decimal.MinusOne, epsilon, -epsilon, formalMax, -formalMax, actualMax, actualMin, epsilon / 10, -epsilon / 10};
                foreach (var testValue in values)
                {
                    param.Value = testValue;

                    await using var reader = await cmd.ExecuteReaderAsync();
                    Assert.True(await reader.ReadAsync());

                    var resultDecimal = reader.GetDecimal(0);
                    var resultStr = reader.GetString(1);

                    Assert.False(await reader.ReadAsync());

                    if (resultStr.StartsWith("--"))
                    {
                        Assert.True(testValue < -formalMax);
                        resultStr = resultStr.Substring(1);
                    }

                    var parsedValue = decimal.Parse(resultStr, CultureInfo.InvariantCulture);
                    if (Math.Abs(testValue) >= epsilon)
                    {
                        Assert.Equal(testValue, resultDecimal);
                        Assert.Equal(testValue, parsedValue);
                    }
                    else
                    {
                        Assert.Equal(0, resultDecimal);
                        Assert.Equal(0, parsedValue);
                    }
                }

                param.Value = actualMax + epsilon;
                var handledException = await Assert.ThrowsAsync<ClickHouseHandledException>(() => cmd.ExecuteScalarAsync());
                Assert.IsType<OverflowException>(handledException.InnerException);

                param.Value = actualMin - epsilon;
                handledException = await Assert.ThrowsAsync<ClickHouseHandledException>(() => cmd.ExecuteScalarAsync());
                Assert.IsType<OverflowException>(handledException.InnerException);
            }

            param.Precision = 18;
            param.Scale = 6;
            {
                const decimal epsilon = 0.000_001m, formalMax = 999_999_999_999.999_999m, actualMax = 9_223_372_036_854.775807m, actualMin = -9_223_372_036_854.775808m;
                var values = new[] { decimal.Zero, decimal.One, decimal.MinusOne, epsilon, -epsilon, formalMax, -formalMax, actualMax, actualMin, epsilon / 10, -epsilon / 10 };
                foreach (var testValue in values)
                {
                    param.Value = testValue;

                    await using var reader = await cmd.ExecuteReaderAsync();
                    Assert.True(await reader.ReadAsync());

                    var resultDecimal = reader.GetDecimal(0);
                    var resultStr = reader.GetString(1);

                    Assert.False(await reader.ReadAsync());

                    if (resultStr.StartsWith("--"))
                    {
                        Assert.True(testValue < -formalMax);
                        resultStr = resultStr.Substring(1);
                    }

                    var parsedValue = decimal.Parse(resultStr, CultureInfo.InvariantCulture);
                    if (Math.Abs(testValue) >= epsilon)
                    {
                        Assert.Equal(testValue, resultDecimal);
                        Assert.Equal(testValue, parsedValue);
                    }
                    else
                    {
                        Assert.Equal(0, resultDecimal);
                        Assert.Equal(0, parsedValue);
                    }
                }

                param.Value = actualMax + epsilon;
                var handledException = await Assert.ThrowsAsync<ClickHouseHandledException>(() => cmd.ExecuteScalarAsync()); 
                Assert.IsType<OverflowException>(handledException.InnerException);

                param.Value = actualMin - epsilon;
                handledException = await Assert.ThrowsAsync<ClickHouseHandledException>(() => cmd.ExecuteScalarAsync());
                Assert.IsType<OverflowException>(handledException.InnerException);
            }

            param.Precision = 35;
            param.Scale = 30;
            {
                const decimal formalMax = 99_999.999_999_999_999_999_999_999_99m, actualMax = 170_141_183.460_469_231_731_687_303_71m, actualMin = -actualMax, epsilon= 0.000_000_000_000_000_000_01m;
                var values = new[] {decimal.Zero, decimal.One, decimal.MinusOne, epsilon, -epsilon, formalMax, -formalMax, actualMax, actualMin};
                foreach (var testValue in values)
                {
                    param.Value = testValue;

                    await using var reader = await cmd.ExecuteReaderAsync();
                    Assert.True(await reader.ReadAsync());

                    var resultDecimal = reader.GetDecimal(0);
                    var resultStr = reader.GetString(1);

                    Assert.False(await reader.ReadAsync());

                    if (resultStr.StartsWith("--"))
                    {
                        Assert.True(testValue < -formalMax);
                        resultStr = resultStr.Substring(1);
                    }

                    var parsedValue = decimal.Parse(resultStr, CultureInfo.InvariantCulture);
                    Assert.Equal(testValue, resultDecimal);
                    Assert.Equal(testValue, parsedValue);
                }

                param.Value = actualMax + epsilon;
                var handledException = await Assert.ThrowsAsync<ClickHouseHandledException>(() => cmd.ExecuteScalarAsync());
                Assert.IsType<OverflowException>(handledException.InnerException);

                param.Value = actualMin - epsilon;
                handledException = await Assert.ThrowsAsync<ClickHouseHandledException>(() => cmd.ExecuteScalarAsync());
                Assert.IsType<OverflowException>(handledException.InnerException);
            }
        }

        [Fact]
        public async Task ClickHouseDecimalTypeNames()
        {
            await using var connection = await OpenConnectionAsync();

            await using var cmd = connection.CreateCommand("SELECT toTypeName({param})");
            var param = new ClickHouseParameter("param") {DbType = DbType.Decimal, Value = 0m};
            cmd.Parameters.Add(param);

            var typeName = await cmd.ExecuteScalarAsync<string>();
            Assert.Equal("Decimal(38, 9)", typeName);

            param.DbType = DbType.Currency;
            typeName = await cmd.ExecuteScalarAsync<string>();
            Assert.Equal("Decimal(18, 4)", typeName);

            param.DbType = DbType.VarNumeric;
            typeName = await cmd.ExecuteScalarAsync<string>();
            Assert.Equal("Decimal(38, 9)", typeName);

            param.Scale = 2;
            param.Precision = 3;
            typeName = await cmd.ExecuteScalarAsync<string>();
            Assert.Equal("Decimal(3, 2)", typeName);

            param.Scale = 14;
            param.Precision = 14;
            typeName = await cmd.ExecuteScalarAsync<string>();
            Assert.Equal("Decimal(14, 14)", typeName);

            param.Scale = 0;
            param.Precision = 1;
            typeName = await cmd.ExecuteScalarAsync<string>();
            Assert.Equal("Decimal(1, 0)", typeName);

            param.Scale = 32;
            param.Precision = 33;
            typeName = await cmd.ExecuteScalarAsync<string>();
            Assert.Equal("Decimal(33, 32)", typeName);
        }

        [Fact]
        public async Task ReadDateTimeParameterScalar()
        {
            var now = DateTime.Now;
            now = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, now.Kind);

            var testData = new[] {now, default, new DateTime(1980, 12, 15, 3, 8, 58), new DateTime(2015, 1, 1, 18, 33, 55)};

            await using var connection = await OpenConnectionAsync();

            await using var cmd = connection.CreateCommand("SELECT {param}");
            var param = new ClickHouseParameter("param");
            cmd.Parameters.Add(param);

            foreach (var testValue in testData)
            {
                param.Value = testValue;

                var result = await cmd.ExecuteScalarAsync<DateTime>();
                Assert.Equal(testValue, result);
            }

            param.Value = DateTime.UnixEpoch.AddMonths(-1);
            var handledException = await Assert.ThrowsAsync<ClickHouseHandledException>(() => cmd.ExecuteScalarAsync());
            Assert.IsType<OverflowException>(handledException.InnerException);

            param.Value = DateTime.UnixEpoch.AddSeconds(uint.MaxValue).AddMonths(1);
            handledException = await Assert.ThrowsAsync<ClickHouseHandledException>(() => cmd.ExecuteScalarAsync());
            Assert.IsType<OverflowException>(handledException.InnerException);
        }

        [Fact]
        public async Task ReadDateTimeOffsetParameterScalar()
        {
            var now = DateTime.Now;
            now = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, now.Kind);

            var testData = new[]
            {
                now, default, new DateTimeOffset(new DateTime(1980, 12, 15, 3, 8, 58), new TimeSpan(0, -5, 0, 0)), new DateTimeOffset(new DateTime(2015, 1, 1, 18, 33, 55), new TimeSpan(0, 3, 15, 0)),
                new DateTimeOffset(DateTime.UnixEpoch.AddSeconds(1)).ToOffset(new TimeSpan(0, -11, 0, 0)),
                new DateTimeOffset(DateTime.UnixEpoch.AddSeconds(uint.MaxValue)).ToOffset(new TimeSpan(0, 11, 0, 0))
            };

            await using var connection = await OpenConnectionAsync();

            await using var cmd = connection.CreateCommand("SELECT {param}");
            var param = new ClickHouseParameter("param");
            cmd.Parameters.Add(param);

            foreach (var testValue in testData)
            {
                param.Value = testValue;

                var result = await cmd.ExecuteScalarAsync<DateTimeOffset>();
                Assert.Equal(0, (result - testValue).Ticks);
            }

            param.Value = DateTimeOffset.UnixEpoch;
            var unixEpochResult = await cmd.ExecuteScalarAsync<DateTimeOffset>();
            Assert.Equal(default, unixEpochResult);

            param.Value = new DateTimeOffset(DateTime.UnixEpoch.AddSeconds(-1)).ToOffset(new TimeSpan(0, -11, 0, 0));
            var handledException = await Assert.ThrowsAsync<ClickHouseHandledException>(() => cmd.ExecuteScalarAsync());
            Assert.IsType<OverflowException>(handledException.InnerException);
            
            param.Value = new DateTimeOffset(DateTime.UnixEpoch.AddSeconds((double) uint.MaxValue + 1)).ToOffset(new TimeSpan(0, 11, 0, 0));
            handledException = await Assert.ThrowsAsync<ClickHouseHandledException>(() => cmd.ExecuteScalarAsync());
            Assert.IsType<OverflowException>(handledException.InnerException);
        }

        [Fact]
        public async Task ReadFloatParameterScalar()
        {
            var testData = new[]
                {float.MinValue, float.MaxValue, float.Epsilon * 2, -float.Epsilon * 2, 1, -1, (float) Math.PI, (float) Math.Exp(1)};

            await using var connection = await OpenConnectionAsync();

            await using var cmd = connection.CreateCommand("SELECT CAST({param}/2 AS Float32)");
            var param = new ClickHouseParameter("param") { DbType = DbType.Single };
            cmd.Parameters.Add(param);

            foreach (var testValue in testData)
            {
                param.Value = testValue;

                var result = await cmd.ExecuteScalarAsync();
                var resultFloat = Assert.IsType<float>(result);

                Assert.Equal(testValue / 2, resultFloat);
            }
        }

        [Fact]
        public async Task ReadDoubleParameterScalar()
        {
            var testData = new[]
                {double.MinValue, double.MaxValue, double.Epsilon * 2, -double.Epsilon * 2, 1, -1, Math.PI, Math.Exp(1)};

            await using var connection = await OpenConnectionAsync();

            await using var cmd = connection.CreateCommand("SELECT {param}/2");
            var param = new ClickHouseParameter("param") { DbType = DbType.Double };
            cmd.Parameters.Add(param);

            foreach (var testValue in testData)
            {
                param.Value = testValue;

                var result = await cmd.ExecuteScalarAsync();
                var resultDouble = Assert.IsType<double>(result);

                Assert.Equal(testValue / 2, resultDouble);
            }
        }

        [Fact]
        public async Task ReadNothingParameterScalar()
        {
            await using var connection = await OpenConnectionAsync();

            await using var cmd = connection.CreateCommand("SELECT {param}");
            var param = new ClickHouseParameter("param");
            cmd.Parameters.Add(param);

            var result = await cmd.ExecuteScalarAsync();
            Assert.Equal(DBNull.Value, result);
        }

        [Fact]
        public async Task ReadIpV4ParameterScalar()
        {
            await using var connection = await OpenConnectionAsync();

            await using var cmd = connection.CreateCommand("SELECT {param}");
            var param = new ClickHouseParameter("param") {Value = IPAddress.Parse("10.0.121.1")};
            Assert.Equal(ClickHouseDbType.IpV4, param.ClickHouseDbType);

            cmd.Parameters.Add(param);
            var result = await cmd.ExecuteScalarAsync();
            Assert.Equal(param.Value, result);

            param.Value = "::ffff:192.0.2.1";
            param.ClickHouseDbType = ClickHouseDbType.IpV4;
            result = await cmd.ExecuteScalarAsync<string>();
            Assert.Equal("192.0.2.1", result);
        }

        [Fact]
        public async Task ReadIpV6ParameterScalar()
        {
            await using var connection = await OpenConnectionAsync();

            await using var cmd = connection.CreateCommand("SELECT {param}");
            var param = new ClickHouseParameter("param") { Value = IPAddress.Parse("2001:0db8:11a3:09d7:1f34:8a2e:07a0:765d") };
            Assert.Equal(ClickHouseDbType.IpV6, param.ClickHouseDbType);

            cmd.Parameters.Add(param);
            var result = await cmd.ExecuteScalarAsync();
            Assert.Equal(param.Value, result);

            param.Value = "192.0.121.234";
            param.ClickHouseDbType = ClickHouseDbType.IpV6;
            result = await cmd.ExecuteScalarAsync<string>();
            Assert.Equal("::ffff:192.0.121.234", result);

            param.Value = null;
            result = await cmd.ExecuteScalarAsync();
            Assert.Equal(DBNull.Value, result);
        }

        [Fact]
        public async Task ReadIntegerParameterScalar()
        {
            object[] values =
            {
                sbyte.MinValue, sbyte.MaxValue, (sbyte) 0,
                byte.MinValue, byte.MaxValue, 
                short.MinValue, short.MaxValue, (short) 0,
                ushort.MinValue, ushort.MaxValue,
                int.MinValue, int.MaxValue, (int) 0,
                uint.MinValue, uint.MaxValue,
                long.MinValue, long.MaxValue, (long) 0, 
                ulong.MinValue, ulong.MaxValue
            };

            await using var connection = await OpenConnectionAsync();

            await using var cmd = connection.CreateCommand("SELECT {integerParam}");
            var param = new ClickHouseParameter("integerParam");
            cmd.Parameters.Add(param);
            foreach (var value in values)
            {
                param.Value = value;
                var result = await cmd.ExecuteScalarAsync();

                Assert.IsType(value.GetType(), result);
                Assert.Equal(result, value);
            }
        }

        [Fact]
        public async Task ReadValueWithOverridenType()
        {
            await WithTemporaryTable("col_settings_type", "id UInt16, ip Nullable(IPv4), enum Nullable(Enum16('min'=-512, 'avg'=0, 'max'=512)), num Nullable(Int32)", RunTest);

            static async Task RunTest(ClickHouseConnection connection, string tableName)
            {
                var cmd = connection.CreateCommand($"INSERT INTO {tableName}(id, ip, enum, num) VALUES" +
                    "(124, null, 'min', 1234)" +
                    "(125, '10.0.0.1', 'avg', null)" +
                    "(126, null, null, null)" +
                    "(127, '127.0.0.1', 'max', -8990)" +
                    "(128, '4.8.15.16', null, 12789)");

                await cmd.ExecuteNonQueryAsync();

                cmd.CommandText = $"SELECT * FROM {tableName} ORDER BY id";
                await using var reader = await cmd.ExecuteReaderAsync();
                var idIdx = reader.GetOrdinal("id");
                var numIdx = reader.GetOrdinal("num");
                var enumIdx = reader.GetOrdinal("enum");
                var ipIdx = reader.GetOrdinal("ip");                

                Assert.Equal(typeof(ushort), reader.GetFieldType(idIdx));
                Assert.Equal(typeof(int), reader.GetFieldType(numIdx));
                Assert.Equal(typeof(string), reader.GetFieldType(enumIdx));
                Assert.Equal(typeof(IPAddress), reader.GetFieldType(ipIdx));

                reader.ConfigureColumn("id", new ClickHouseColumnSettings(typeof(int)));
                reader.ConfigureColumn("num", new ClickHouseColumnSettings(typeof(long)));
                reader.ConfigureColumn("enum", new ClickHouseColumnSettings(typeof(short?)));
                reader.ConfigureColumn("ip", new ClickHouseColumnSettings(typeof(string)));

                Assert.Equal(typeof(int), reader.GetFieldType(idIdx));
                Assert.Equal(typeof(long), reader.GetFieldType(numIdx));
                Assert.Equal(typeof(short), reader.GetFieldType(enumIdx));
                Assert.Equal(typeof(string), reader.GetFieldType(ipIdx));

                var expectedData = new object[][]
                {
                    new object[] {124, DBNull.Value, (short)-512, 1234L},
                    new object[] {125, "10.0.0.1", (short)0, DBNull.Value},
                    new object[] {126, DBNull.Value, DBNull.Value, DBNull.Value},
                    new object[] {127, "127.0.0.1", (short)512, -8990L},
                    new object[] {128, "4.8.15.16", DBNull.Value, 12789L}
                };

                int count = 0;
                while(await reader.ReadAsync())
                {
                    Assert.Equal(expectedData[count][0], reader.GetValue(idIdx));
                    Assert.Equal(expectedData[count][1], reader.GetValue(ipIdx));
                    Assert.Equal(expectedData[count][2], reader.GetValue(enumIdx));
                    Assert.Equal(expectedData[count][3], reader.GetValue(numIdx));

                    ++count;
                }

                Assert.Equal(expectedData.Length, count);                
            }
        }

        [Fact]
        public async Task ReadGuidColumn()
        {
            var guids = new List<Guid> { Guid.Parse("74D47928-2423-4FE2-AD45-82E296BF6058"), Guid.Parse("2879D474-2324-E24F-AD45-82E296BF6058"), Guid.Empty };
            guids.AddRange(Enumerable.Range(1, 100 - guids.Count).Select(_ => Guid.NewGuid()));

            await WithTemporaryTable("uuid", "id Int32, guid UUID, str String", RunTest);

            async Task RunTest(ClickHouseConnection connection, string tableName)
            {
                await using (var writer = await connection.CreateColumnWriterAsync($"INSERT INTO {tableName}(id, guid, str) VALUES", CancellationToken.None))
                {
                    await writer.WriteTableAsync(new object[] { Enumerable.Range(0, guids.Count), guids, guids.Select(v => v.ToString("D")) }, guids.Count, CancellationToken.None);
                }

                var cmd = connection.CreateCommand($"SELECT id, guid, CAST(guid AS String) strGuid, (guid = CAST(str AS UUID)) eq FROM {tableName} ORDER BY id");

                await using var reader = await cmd.ExecuteReaderAsync();

                int count = 0;
                while (await reader.ReadAsync())
                {
                    var id = reader.GetInt32(0);
                    var guid = reader.GetGuid(1);
                    var str = reader.GetString(2);
                    var eq = reader.GetBoolean(3);

                    Assert.Equal(count, id);
                    Assert.Equal(guids[id], guid);
                    Assert.True(Guid.TryParse(str, out var strGuid));
                    Assert.Equal(guids[id], strGuid);
                    Assert.True(eq);

                    ++count;
                }

                Assert.Equal(guids.Count, count);
            }
        }

        [Fact]
        public async Task CreateInsertSelectAllKnownNullable()
        {
            const string ddl = @"
CREATE TABLE clickhouse_test_nullable (
    int8     Nullable(Int8),
    int16    Nullable(Int16),
    int32    Nullable(Int32),
    int64    Nullable(Int64),
    uint8    Nullable(UInt8),
    uint16   Nullable(UInt16),
    uint32   Nullable(UInt32),
    uint64   Nullable(UInt64),
    float32  Nullable(Float32),
    float64  Nullable(Float64),
    string   Nullable(String),
    fString  Nullable(FixedString(2)),
    date     Nullable(Date),
    datetime Nullable(DateTime),
    enum8    Nullable(Enum8 ('a' = 127, 'b' = 2)),
    enum16   Nullable(Enum16('c' = -32768, 'd' = 2, '' = 42))
) Engine=Memory;";

            const string dml = @"
INSERT INTO clickhouse_test_nullable (
    int8
    ,int16
    ,int32
    ,int64
    ,uint8
    ,uint16
    ,uint32
    ,uint64
    ,float32
    ,float64
    ,string
    ,fString
    ,date
    ,datetime
    ,enum8
    ,enum16
) VALUES (
    8
    ,16
    ,32
    ,64
    ,18
    ,116
    ,132
    ,165
    ,1.1
    ,1.2
    ,'RU'
    ,'UA'
    ,now()
    ,now()
    ,'a'
    ,'c'
)";

            const string query = @"
SELECT
    int8
    ,int16
    ,int32
    ,int64
    ,uint8
    ,uint16
    ,uint32
    ,uint64
    ,float32
    ,float64
    ,string
    ,fString
    ,date
    ,datetime
    ,enum8
    ,enum16
FROM clickhouse_test_nullable";

            try
            {
                await using var connection = await OpenConnectionAsync();
                
                await using var cmdDrop = connection.CreateCommand("DROP TABLE IF EXISTS clickhouse_test_nullable ");
                var ddlResult = await cmdDrop.ExecuteNonQueryAsync();
                Assert.Equal(0, ddlResult);

                await using var cmd = connection.CreateCommand(ddl);
                var result = await cmd.ExecuteNonQueryAsync();
                Assert.Equal(0, result);

                await using var dmlcmd = connection.CreateCommand(dml);
                result = await dmlcmd.ExecuteNonQueryAsync();
                Assert.Equal(1, result);

                await using var queryCmd = connection.CreateCommand(query);
                var r = await queryCmd.ExecuteReaderAsync();
                while (r.Read())
                {
                    Assert.Equal(typeof(sbyte), r.GetFieldType(0));
                    Assert.Equal((sbyte) 8, r.GetFieldValue<sbyte>(0)); //int8
                    Assert.Equal((sbyte) 8, r.GetValue(0));

                    Assert.Equal(typeof(short), r.GetFieldType(1));
                    Assert.Equal((short) 16, r.GetInt16(1));
                    Assert.Equal((short) 16, r.GetValue(1));

                    Assert.Equal(typeof(int), r.GetFieldType(2));
                    Assert.Equal((int) 32, r.GetInt32(2));
                    Assert.Equal((int) 32, r.GetValue(2));

                    Assert.Equal(typeof(long), r.GetFieldType(3));
                    Assert.Equal((long) 64, r.GetInt64(3));
                    Assert.Equal((long) 64, r.GetValue(3));

                    Assert.Equal(typeof(byte), r.GetFieldType(4));
                    Assert.Equal((byte) 18, r.GetFieldValue<byte>(4)); //uint8
                    Assert.Equal((byte) 18, r.GetValue(4));

                    Assert.Equal(typeof(ushort), r.GetFieldType(5));
                    Assert.Equal((ushort) 116, r.GetFieldValue<ushort>(5));
                    Assert.Equal((ushort) 116, r.GetValue(5));

                    Assert.Equal(typeof(uint), r.GetFieldType(6));
                    Assert.Equal((uint) 132, r.GetFieldValue<uint>(6));
                    Assert.Equal((uint) 132, r.GetValue(6));

                    Assert.Equal(typeof(ulong), r.GetFieldType(7));
                    Assert.Equal((UInt64) 165, r.GetFieldValue<UInt64>(7));
                    Assert.Equal((UInt64) 165, r.GetValue(7));

                    Assert.Equal(typeof(float), r.GetFieldType(8));
                    Assert.Equal((float) 1.1, r.GetFloat(8));
                    Assert.Equal((float) 1.1, r.GetValue(8));

                    Assert.Equal(typeof(double), r.GetFieldType(9));
                    Assert.Equal((double) 1.2, r.GetDouble(9));
                    Assert.Equal((double) 1.2, r.GetValue(9));

                    Assert.Equal(typeof(string), r.GetFieldType(10));
                    Assert.Equal("RU", r.GetString(10));
                    Assert.Equal("RU", r.GetValue(10));

                    Assert.Equal(typeof(byte[]), r.GetFieldType(11));
                    var fixedStringBytes = r.GetFieldValue<byte[]>(11);
                    var fixedStringBytesAsValue = r.GetValue(11) as byte[];
                    Assert.Equal(fixedStringBytes, fixedStringBytesAsValue);
                    Assert.NotNull(fixedStringBytes as byte[]);
                    Assert.Equal("UA", Encoding.Default.GetString(fixedStringBytes as byte[]));

                    Assert.Equal(typeof(string), r.GetFieldType(14));
                    Assert.Equal("a", r.GetValue(14));
                    Assert.Equal(127, r.GetFieldValue<sbyte>(14));

                    Assert.Equal(typeof(string), r.GetFieldType(15));
                    Assert.Equal("c", r.GetValue(15));
                    Assert.Equal(-32768, r.GetInt16(15));
                }
            }
            finally
            {
                await using var connection = await OpenConnectionAsync();
                await using var cmdDrop = connection.CreateCommand("DROP TABLE IF EXISTS clickhouse_test_nullable ");
                await cmdDrop.ExecuteNonQueryAsync();
            }
        }
    }
}
