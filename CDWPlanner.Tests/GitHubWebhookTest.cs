using CDWPlaner;
using CDWPlaner.DTO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Moq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace CDWPlaner.Tests
{
    public class GitHubWebhookTest
    {
        [Fact]
        public async Task SingleCommitSingleYaml()
        {
            var githubWebhookRequestJson = @"
            {
              ""commits"": [
                {
                  ""id"": ""13c178b8ebe91815e59d44aec2f593570d5d00e3"",
                  ""added"": [
                  ],
                  ""removed"": [
                  ],
                  ""modified"": [
                    ""2020-07-17/PLAN.yml""
                  ]
                }
              ]
            }";

            WorkshopOperation operation = null;
            using var githubWebhookRequest = new MockHttpRequest(githubWebhookRequestJson);
            var collector = new Mock<ICollector<WorkshopOperation>>();
            collector.Setup(c => c.Add(It.IsAny<WorkshopOperation>()))
                .Callback<WorkshopOperation>(wo => operation = wo)
                .Verifiable();

            var logger = Mock.Of<ILogger>();

            var fileReader = new Mock<IGitHubFileReader>();
            fileReader.Setup(fr => fr.GetYMLFileFromGitHub(It.IsAny<FolderFileInfo>(), It.IsAny<string>()))
                .Returns(Task.FromResult(new WorkshopsRoot()))
                .Verifiable();

            var planEvent = new PlanEvent(fileReader.Object, null);
            var result = await planEvent.ReceiveFromGitHub(githubWebhookRequest.HttpRequestMock.Object, collector.Object, logger);

            Assert.IsType<AcceptedResult>(result);
            collector.Verify(c => c.Add(It.IsAny<WorkshopOperation>()), Times.Once);
            fileReader.Verify(fr => fr.GetYMLFileFromGitHub(It.IsAny<FolderFileInfo>(), It.IsAny<string>()), Times.Once);
            Assert.NotNull(operation);
            Assert.Equal("PLAN.yml", operation.FolderInfo.File);
            Assert.Equal("2020-07-17", operation.FolderInfo.DateFolder);
            Assert.Equal("2020-07-17/PLAN.yml", operation.FolderInfo.FullFolder);
            Assert.Equal("modified", operation.Operation);
        }

        [Fact]
        public async Task MultipleCommitsMultipleYamls()
        {
            var githubWebhookRequestJson = @"
            {
              ""commits"": [
                {
                  ""id"": ""13c178b8ebe91815e59d44aec2f593570d5d00e3"",
                  ""added"": [
                  ],
                  ""removed"": [
                  ],
                  ""modified"": [
                    ""2020-07-17/PLAN.yml""
                  ]
                },
                {
                  ""id"": ""13c178b8ebe91815e59d44aec2f593570d5d00f3"",
                  ""added"": [
                    ""2020-07-18/PLAN.yml""
                  ],
                  ""removed"": [
                  ],
                  ""modified"": [
                  ]
                }
              ]
            }";

            var operations = new List<WorkshopOperation>();
            using var githubWebhookRequest = new MockHttpRequest(githubWebhookRequestJson);
            var collector = new Mock<ICollector<WorkshopOperation>>();
            collector.Setup(c => c.Add(It.IsAny<WorkshopOperation>()))
                .Callback<WorkshopOperation>(wo => operations.Add(wo))
                .Verifiable();

            var logger = Mock.Of<ILogger>();

            var fileReader = new Mock<IGitHubFileReader>();
            fileReader.Setup(fr => fr.GetYMLFileFromGitHub(It.IsAny<FolderFileInfo>(), It.IsAny<string>()))
                .Returns(Task.FromResult(new WorkshopsRoot()))
                .Verifiable();

            var planEvent = new PlanEvent(fileReader.Object, null);
            var result = await planEvent.ReceiveFromGitHub(githubWebhookRequest.HttpRequestMock.Object, collector.Object, logger);

            Assert.IsType<AcceptedResult>(result);
            collector.Verify(c => c.Add(It.IsAny<WorkshopOperation>()), Times.Exactly(2));
            fileReader.Verify(fr => fr.GetYMLFileFromGitHub(It.IsAny<FolderFileInfo>(), It.IsAny<string>()), Times.Exactly(2));
            Assert.Equal(2, operations.Count);
            Assert.Equal("PLAN.yml", operations[0].FolderInfo.File);
            Assert.Equal("2020-07-17", operations[0].FolderInfo.DateFolder);
            Assert.Equal("2020-07-17/PLAN.yml", operations[0].FolderInfo.FullFolder);
            Assert.Equal("modified", operations[0].Operation);
            Assert.Equal("PLAN.yml", operations[1].FolderInfo.File);
            Assert.Equal("2020-07-18", operations[1].FolderInfo.DateFolder);
            Assert.Equal("2020-07-18/PLAN.yml", operations[1].FolderInfo.FullFolder);
            Assert.Equal("added", operations[1].Operation);
        }

        [Fact]
        public void BuildEventDocument()
        {
            var builtEvent = PlanEvent.BuildEventDocument(new DateTime(2020, 12, 31),
                new BsonArray(new[] { "Foo", "Bar" }));

            Assert.Equal(new DateTime(2020, 12, 31), builtEvent["date"]);
            Assert.Equal("CoderDojo Virtual", builtEvent["type"]);
            Assert.Equal("CoderDojo Online", builtEvent["location"]);
            Assert.Equal(new BsonArray(new[] { "Foo", "Bar" }), builtEvent["workshops"]);
        }

        [Fact]
        public void BuildEventDocumentWithoutWorkshops()
        {
            var builtEvent = PlanEvent.BuildEventDocument(new DateTime(2020, 12, 31),
                new BsonArray());

            builtEvent["location"] = "CoderDojo Online";
            Assert.True(new BsonArray().Count == 0 || new BsonArray() == null);
            builtEvent["location"] += " - Themen werden noch bekannt gegeben";
        }

        [Fact]
        public void AddWorkshopHtmlTest()
        {
            var ws = BsonValue.Create(new
            {
                begintime = new DateTime(2020, 1, 1, 13, 0, 0, DateTimeKind.Utc).ToString().Replace(":00Z", string.Empty).Split("T").Last(),
                endtime = new DateTime(2020, 1, 1, 14, 0, 0, DateTimeKind.Utc).ToString().Replace(":00Z", string.Empty).Split("T").Last(),
                description = "*Bar*",
                title = "Foo",
                targetAudience = "FooBar",
        });
            var builder = new StringBuilder();
            PlanEvent.AddWorkshopHtml(builder, ws);

            Assert.Equal(
                "\n<h3>Foo</h3>\n<p class=subtitle'>13:0014:00<br/>\nFooBar</p>\n<p><b>Bar<b/></p>",
                builder.ToString());
        }
    }
}