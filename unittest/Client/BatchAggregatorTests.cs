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

namespace MetricSystem.Client.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using MetricSystem.Utilities;

    using NUnit.Framework;

    [TestFixture]
    public class BatchAggregatorTests
    {
        private readonly long sampleStartTime = DateTime.Now.ToMillisecondTimestamp();
        private readonly long sampleEndTime = DateTime.Now.AddMinutes(20).ToMillisecondTimestamp();

        [Test]
        public void AggregatorValidatesNullOrMissingArguments()
        {
            Assert.Throws<ArgumentNullException>(() => new BatchResponseAggregator(null));
            Assert.Throws<ArgumentException>(() => new BatchResponseAggregator(new BatchQueryRequest()));
        }

        [Test]
        public void AggregatorFixesUpQueryParametersIfNeeded()
        {
            var counterQuery = new BatchCounterQuery();
            counterQuery.QueryParameters.Add("percentile", "50");
            
            var batchRequest = new BatchQueryRequest();
            batchRequest.Queries.Add(counterQuery);

            new BatchResponseAggregator(batchRequest);

            // percentile should have been removed to downstream requests
            Assert.IsFalse(counterQuery.QueryParameters.ContainsKey("percentile"));

            // user context should have been filled in as a guid
            Assert.IsFalse(string.IsNullOrEmpty(counterQuery.UserContext));
        }

        [Test]
        public void AggregatorAggregatesCorrectlyWhenNoOverlaps()
        {
            var aCounter = new BatchCounterQuery { CounterName = "a", UserContext = "a" };
            var bCounter = new BatchCounterQuery { CounterName = "b", UserContext = "b" };

            var batchRequest = new BatchQueryRequest();
            batchRequest.Queries.Add(aCounter);
            batchRequest.Queries.Add(bCounter);

            var agg = new BatchResponseAggregator(batchRequest);

            var aOnlyResponse = new BatchQueryResponse
                                {
                                    Responses = {this.CreateHitCountResponse("a", 100)},
                                    RequestDetails =
                                    {
                                        new RequestDetails
                                        {
                                            Server = new ServerInfo
                                            {
                                                Hostname = "something",
                                                Port = 42
                                            },
                                            HttpResponseCode = 200
                                        }
                                    }
                                };
            var bOnlyResponse = new BatchQueryResponse
                                {
                                    Responses = {this.CreateHitCountResponse("b", 200)},
                                    RequestDetails =
                                    {
                                        new RequestDetails
                                        {
                                            Server = new ServerInfo
                                            {
                                                Hostname = "somewhere",
                                                Port = 42
                                            },
                                            HttpResponseCode = 200
                                        }
                                    }
                                };

            agg.AddResponse(aOnlyResponse);
            agg.AddResponse(bOnlyResponse);

            var finalResponse = agg.GetResponse();
            Assert.IsNotNull(finalResponse);
            Assert.AreEqual(2, finalResponse.RequestDetails.Count);
            Assert.AreEqual(2, finalResponse.Responses.Count);
            Assert.AreEqual(1, finalResponse.Responses.Count(x => x.UserContext.Equals("a") && x.Samples[0].HitCount == 100));
            Assert.AreEqual(1, finalResponse.Responses.Count(x => x.UserContext.Equals("b") && x.Samples[0].HitCount == 200));
        }

        [Test]
        public void AggregatorAggregatesOverlapCorrectly()
        {
            var aCounter = new BatchCounterQuery { CounterName = "a", UserContext = "a" };

            var batchRequest = new BatchQueryRequest();
            batchRequest.Queries.Add(aCounter);

            var agg = new BatchResponseAggregator(batchRequest);

            var oneResponse = new BatchQueryResponse
                              {
                                  Responses = {this.CreateHitCountResponse("a", 100)},
                                  RequestDetails =
                                  {
                                      new RequestDetails
                                      {
                                          Server = new ServerInfo
                                          {
                                              Hostname = "something",
                                              Port = 42
                                          },
                                          HttpResponseCode = 200
                                      }
                                  }
                              };
            var twoResponse = new BatchQueryResponse
                              {
                                  Responses = {this.CreateHitCountResponse("a", 200)},
                                  RequestDetails =
                                  {
                                      new RequestDetails
                                      {
                                          Server = new ServerInfo
                                          {
                                              Hostname = "somewhere",
                                              Port = 42
                                          },
                                          HttpResponseCode = 200
                                      }
                                  }
                              };

            agg.AddResponse(oneResponse);
            agg.AddResponse(twoResponse);

            var finalResponse = agg.GetResponse();
            Assert.IsNotNull(finalResponse);
            Assert.AreEqual(2, finalResponse.RequestDetails.Count);
            Assert.AreEqual(1, finalResponse.Responses.Count);
            Assert.AreEqual(300, finalResponse.Responses[0].Samples[0].HitCount);
        }

        [Test]
        public void AggregatorIgnoresUnknownCounterInResponse()
        {
            var aCounter = new BatchCounterQuery { CounterName = "a", UserContext = "a" };

            var batchRequest = new BatchQueryRequest();
            batchRequest.Queries.Add(aCounter);

            var agg = new BatchResponseAggregator(batchRequest);

            var oneResponse = new BatchQueryResponse
                              {
                                  Responses = {this.CreateHitCountResponse("i am a key tree", 100)},
                                  RequestDetails =
                                  {
                                      new RequestDetails
                                      {
                                          Server = new ServerInfo
                                                   {
                                                       Hostname = "something",
                                                   },
                                          HttpResponseCode = 200
                                      }
                                  }
                              };

            agg.AddResponse(oneResponse);

            var finalResponse = agg.GetResponse();
            Assert.IsNotNull(finalResponse);
            Assert.AreEqual(1, finalResponse.Responses.Count);
            Assert.AreEqual(0, finalResponse.Responses[0].Samples.Count);
        }

        private CounterQueryResponse CreateHitCountResponse(string userContext, int hitCount = 10)
        {
            return new CounterQueryResponse
                   {
                       HttpResponseCode = (short)200,
                       UserContext = userContext,
                       Samples = new List<DataSample>
                                 {
                                     new DataSample
                                     {
                                         HitCount = hitCount,
                                         SampleType = DataSampleType.HitCount,
                                         StartTime = this.sampleStartTime,
                                         EndTime = this.sampleEndTime,
                                         Dimensions = new Dictionary<string, string>()
                                     }
                                 }
                   };
        }

        private CounterQueryResponse CreateFailedCounterResponse(string userContext)
        {
            return new CounterQueryResponse
            {
                HttpResponseCode = (short)404,
                ErrorMessage = "Not Found",
                UserContext = userContext,
            };
            
        }
    }
}
