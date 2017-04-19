﻿// The MIT License (MIT)
// 
// Copyright (c) 2015 Microsoft
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

namespace MetricSystem.Data.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;

    using Microsoft.IO;

    using NUnit.Framework;

    [TestFixture]
    public sealed class PersistedDataTests
    {
        private const string AnyDimension = "foo";
        const string AnyDimensionValue = "bar";
        private const string AnyCounterName = "/Foo";
        private const long AnyDataValue = 8675309;
        private static readonly DateTime AnyStart = DateTime.MinValue;
        private static readonly DateTime AnyEnd = DateTime.MaxValue;
        private static readonly IEnumerable<PersistedDataSource> AnySources = new[]
        {
            new PersistedDataSource("Foo", PersistedDataSourceStatus.Available)
        };

        private DimensionSet dimensions;
        private KeyedDataStore<InternalHitCount> data;
        private MemoryStream stream;
        private RecyclableMemoryStreamManager streamManager;
            
        [SetUp]
        public void SetUp()
        {
            this.stream = new MemoryStream();
            this.streamManager = new RecyclableMemoryStreamManager(1 << 17, 2, 1 << 24);
            this.dimensions = new DimensionSet(new HashSet<Dimension>(new[] {new Dimension(AnyDimension)}));
            this.data = new KeyedDataStore<InternalHitCount>(this.dimensions,
                                                                                        this.streamManager);
            var hitCount = new InternalHitCount();
            hitCount.AddValue(AnyDataValue);
            this.data.AddValue(new DimensionSpecification {{AnyDimension, AnyDimensionValue}}, AnyDataValue);
            this.data.Merge();
        }

        [TearDown]
        public void TearDown()
        {
            if (this.data != null)
            {
                this.data.Dispose();
                this.data = null;
            }

            if (this.stream != null)
            {
                this.stream.Dispose();
                this.stream = null;
            }
        }

        [Test]
        public void CanReadWrittenData()
        {
            using (var writer = new PersistedDataWriter(stream, dimensions, this.streamManager))
            {
                writer.WriteData(AnyCounterName, AnyStart, AnyEnd, (uint)data.Count, AnySources, data);
            }

            stream.Position = 0;
            using (var reader = new PersistedDataReader(stream, this.streamManager))
            {
                Assert.IsTrue(reader.ReadDataHeader());
                Assert.AreEqual("/Foo", reader.Header.Name);
                Assert.AreEqual((uint)data.Count, reader.Header.DataCount);
                reader.ReadData<InternalHitCount>(
                    (key, value) => Assert.AreEqual(AnyDataValue, value.HitCount));
            }
        }

        [Test]
        public void ReadingTruncatedDataThrowsPersistedDataException()
        {
            using (var writer = new PersistedDataWriter(stream, dimensions, this.streamManager))
            {
                writer.WriteData(AnyCounterName, AnyStart, AnyEnd, (uint)data.Count, AnySources, data);
            }

            for (int i = (int)stream.Length / 2; i >= 0; --i)
            {
                var buffer = new byte[i];
                Buffer.BlockCopy(stream.GetBuffer(), 0, buffer, 0, i);
                using (var truncatedStream = new MemoryStream(buffer, false))
                using (var reader = new PersistedDataReader(truncatedStream, this.streamManager))
                {
                    try
                    {
                        // If we can't read the header it's fine.
                        if (reader.ReadDataHeader())
                        {
                            reader.ReadData<InternalHitCount>((key, value) => { });
                            Assert.Fail();
                        }
                    }
                    catch (PersistedDataException) {} 
                }
            }
        }

        [Test]
        public void CanReadMultipleWrittenDataFromSameStream()
        {
            const int iterations = 10;
            for (int i = 0; i < iterations; ++i)
            {
                using (var writer = new PersistedDataWriter(stream, dimensions, this.streamManager))
                {
                    writer.WriteData(AnyCounterName, AnyStart, AnyEnd, (uint)data.Count, AnySources, data);
                }
            }

            stream.Position = 0;
            using (var reader = new PersistedDataReader(stream, this.streamManager))
            {
                for (int i = 0; i < iterations; ++i)
                {
                    Assert.IsTrue(reader.ReadDataHeader());
                    Assert.AreEqual(reader.Header.Name, "/Foo");
                    Assert.AreEqual((uint)data.Count, reader.Header.DataCount);
                    reader.ReadData<InternalHitCount>(
                         (key, value) => Assert.AreEqual(AnyDataValue, value.HitCount));
                }
            }
        }

        [TestCase(@"TestData\legacy_histogram.msdata")]
        [TestCase(@"TestData\legacy_hitcount.msdata")]
        public void CanReadLegacyDataAndRewriteInNewFormat(string originalFilename)
        {
            var tempFile = Path.GetTempFileName();

            var testDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var sourceFile = Path.Combine(testDir, originalFilename);
            using (var sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read))
            using (var sourceReader = new PersistedDataReader(sourceStream, this.streamManager))
            using (var destStream = new FileStream(tempFile, FileMode.Open, FileAccess.Write))
            {
                while (sourceReader.ReadDataHeader())
                {
                    using (var destWriter = new PersistedDataWriter(destStream, sourceReader.DimensionSet,
                                                                    this.streamManager))
                    {
                        if (sourceReader.DataType == typeof(InternalHitCount))
                        {
                            destWriter.WriteData(sourceReader.Header.Name, sourceReader.StartTime, sourceReader.EndTime,
                                                 sourceReader.Header.DataCount, sourceReader.Header.Sources,
                                                 sourceReader.LoadData<InternalHitCount>());
                        }
                        else 
                        {
                            destWriter.WriteData(sourceReader.Header.Name, sourceReader.StartTime, sourceReader.EndTime,
                                                 sourceReader.Header.DataCount, sourceReader.Header.Sources,
                                                 sourceReader.LoadData<InternalHistogram>());

                        }
                    }
                }
            }

            this.VerifyMatchingContents(sourceFile, tempFile);
            File.Delete(tempFile);
        }

        private void VerifyMatchingContents(string left, string right)
        {
            using (var leftStream = new FileStream(left, FileMode.Open, FileAccess.Read))
            using (var leftReader = new PersistedDataReader(leftStream, this.streamManager))
            using (var rightStream = new FileStream(right, FileMode.Open, FileAccess.Read))
            using (var rightReader = new PersistedDataReader(rightStream, this.streamManager))
            {
                while (leftReader.ReadDataHeader())
                {
                    rightReader.ReadDataHeader();
                    Assert.IsFalse(leftReader.IsLatestProtocol);
                    Assert.IsTrue(rightReader.IsLatestProtocol);
                    Assert.AreEqual(leftReader.Header.DataCount, rightReader.Header.DataCount);
                    Assert.IsTrue(leftReader.DimensionSet.Equals(rightReader.DimensionSet));
                    Assert.AreEqual(leftReader.StartTime, rightReader.StartTime);
                    Assert.AreEqual(leftReader.EndTime, rightReader.EndTime);

                    if (leftReader.DataType == typeof(InternalHitCount))
                    {
                        Assert.AreEqual(leftReader.Header.DataType, rightReader.Header.DataType);

                        var leftData = leftReader.LoadData<InternalHitCount>().ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value.HitCount);
                        var rightData = rightReader.LoadData<InternalHitCount>().ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value.HitCount);

                        Assert.AreEqual(leftData.Count, rightData.Count);
                        Assert.IsTrue(leftData.All(kvp => rightData[kvp.Key] == kvp.Value));
                    }
                    else
                    {
                        var leftData = leftReader.LoadData<InternalHistogram>()
                                                 .ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value.Data);
                        var rightData = rightReader.LoadData<InternalHistogram>()
                            .ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value.Data);

                        Assert.AreEqual(leftData.Count, rightData.Count);
                        Assert.IsTrue(leftData.All(
                                                   kvp =>
                                                   {
                                                       var otherData = rightData[kvp.Key];
                                                       return otherData.Count == kvp.Value.Count &&
                                                              kvp.Value.All(x => otherData[x.Key] == x.Value);
                                                   }
                            ));
                    }
                }
            }
        }
    }
}
