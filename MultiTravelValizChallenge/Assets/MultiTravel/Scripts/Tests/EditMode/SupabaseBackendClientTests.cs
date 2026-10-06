using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using MultiTravel.Core.Backend;
using MultiTravel.Core.Session;
using MultiTravel.Tests.EditMode.Fakes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MultiTravel.Tests.EditMode
{
    /// <summary>
    /// <see cref="SupabaseBackendClient"/> against <see cref="FakeHttpTransport"/> (synchronous; no network, no Unity
    /// main-thread continuations). Parameter names are checked against ARCHITECTURE.md §4 and, when the repository
    /// layout is present, against <c>backend/supabase/migrations/*.sql</c>.
    /// </summary>
    public sealed class SupabaseBackendClientTests
    {
        private static readonly string[] RegisterParams =
        {
            "p_event_slug", "p_access_code", "p_station_id", "p_client_session_id", "p_first_name", "p_last_name",
            "p_title", "p_company", "p_location", "p_phone", "p_email", "p_gender", "p_consent_accepted", "p_consent_version"
        };

        private static readonly string[] SubmitParams =
        {
            "p_event_slug", "p_access_code", "p_station_id", "p_submission_id", "p_client_session_id", "p_participant",
            "p_score", "p_completion_ms", "p_correct_count", "p_incorrect_count", "p_required_total",
            "p_placed_product_ids", "p_gender", "p_status", "p_completion_reason", "p_completed_at", "p_client_version"
        };

        private static readonly string[] LeaderboardParams = { "p_event_slug", "p_limit" };

        private static readonly string[] PingParams = { "p_event_slug", "p_access_code" };

        /// <summary>Keys read by <c>mt_clean_participant(p jsonb)</c>.</summary>
        private static readonly string[] ParticipantJsonKeys =
        {
            "station_id", "first_name", "last_name", "phone", "email", "gender", "consent_accepted", "consent_version"
        };

        private const string SubmitOkBody =
            "{\"result_id\":\"11111111-1111-1111-1111-111111111111\",\"participant_id\":\"22222222-2222-2222-2222-222222222222\",\"created\":true,\"rank\":4}";

        private FakeHttpTransport transport;

        [SetUp]
        public void SetUp()
        {
            transport = new FakeHttpTransport();
        }

        private SupabaseBackendClient Client(
            string url = TestData.Url,
            string anonKey = TestData.AnonKey,
            string accessCode = TestData.AccessCode,
            string eventSlug = TestData.EventSlug,
            int timeoutSeconds = 7)
        {
            var config = TestData.Config(url: url, anonKey: anonKey, accessCode: accessCode, eventSlug: eventSlug, requestTimeoutSeconds: timeoutSeconds);
            return new SupabaseBackendClient(config, transport);
        }

        private static JObject ParseBody(string json)
        {
            // DateParseHandling.None keeps timestamps as the exact wire strings.
            return JsonConvert.DeserializeObject<JObject>(json, new JsonSerializerSettings { DateParseHandling = DateParseHandling.None });
        }

        private static string[] Keys(JObject obj)
        {
            var keys = new List<string>();
            foreach (var property in obj.Properties())
            {
                keys.Add(property.Name);
            }

            return keys.ToArray();
        }

        private BackendError SubmitExpectingError()
        {
            var result = SyncTask.Completed(Client().SubmitResultAsync(TestData.Payload()));
            Assert.IsFalse(result.Ok);
            Assert.IsNotNull(result.Error);
            return result.Error;
        }

        // ----- request shape -----

        [Test]
        public void EveryCall_UsesRpcUrlAndHeaders()
        {
            transport.EnqueueJson(200, SubmitOkBody);
            transport.EnqueueJson(200, "{\"participant_id\":\"22222222-2222-2222-2222-222222222222\",\"created\":true}");
            transport.EnqueueJson(200, "[]");
            transport.EnqueueJson(200, "{\"ok\":true,\"event_name\":\"Test\",\"server_time\":\"2026-10-01T10:00:00+00:00\"}");
            var client = Client(url: TestData.Url + "/", timeoutSeconds: 7);

            SyncTask.Completed(client.SubmitResultAsync(TestData.Payload()));
            SyncTask.Completed(client.RegisterAsync(TestData.CompletedSession()));
            SyncTask.Completed(client.GetLeaderboardAsync(10));
            SyncTask.Completed(client.PingAsync());

            var expectedUrls = new[]
            {
                TestData.Url + "/rest/v1/rpc/submit_result",
                TestData.Url + "/rest/v1/rpc/register_participant",
                TestData.Url + "/rest/v1/rpc/get_leaderboard",
                TestData.Url + "/rest/v1/rpc/ping_event"
            };

            Assert.AreEqual(4, transport.Requests.Count);
            for (int i = 0; i < expectedUrls.Length; i++)
            {
                var request = transport.Requests[i];
                Assert.AreEqual(expectedUrls[i], request.Url, "trailing slash of SupabaseUrl is trimmed");
                Assert.AreEqual(7, request.TimeoutSeconds);
                Assert.AreEqual(TestData.AnonKey, request.Headers["apikey"]);
                Assert.AreEqual("Bearer " + TestData.AnonKey, request.Headers["Authorization"]);
                Assert.AreEqual("application/json", request.Headers["Content-Type"]);
            }
        }

        [Test]
        public void RegisterBody_HasExactlyTheRpcParameters_WithNormalisedValues()
        {
            transport.EnqueueJson(200, "{\"participant_id\":\"22222222-2222-2222-2222-222222222222\",\"created\":false}");
            var session = TestData.CompletedSession(gender: Gender.Male);

            var result = SyncTask.Completed(Client().RegisterAsync(session));

            Assert.IsTrue(result.Ok, result.ToString());
            Assert.AreEqual(new Guid("22222222-2222-2222-2222-222222222222"), result.Value.ParticipantId);
            Assert.IsFalse(result.Value.Created);

            var body = ParseBody(transport.LastRequest.Body);
            CollectionAssert.AreEquivalent(RegisterParams, Keys(body));
            Assert.AreEqual(TestData.EventSlug, (string)body["p_event_slug"]);
            Assert.AreEqual(TestData.AccessCode, (string)body["p_access_code"]);
            Assert.AreEqual(TestData.StationId, (string)body["p_station_id"]);
            Assert.AreEqual(session.ClientSessionId.ToString("D"), (string)body["p_client_session_id"]);
            Assert.AreEqual(TestData.PiiFirstName, (string)body["p_first_name"]);
            Assert.AreEqual(TestData.PiiLastName, (string)body["p_last_name"]);
            Assert.AreEqual(TestData.PiiPhone, (string)body["p_phone"]);
            Assert.AreEqual(TestData.PiiEmail, (string)body["p_email"]);
            Assert.AreEqual("male", (string)body["p_gender"]);
            Assert.AreEqual(true, (bool?)body["p_consent_accepted"]);
            Assert.AreEqual("1.0", (string)body["p_consent_version"]);
        }

        [Test]
        public void RegisterBody_WithoutConsent_SendsExplicitNulls()
        {
            transport.EnqueueJson(200, "{\"participant_id\":\"22222222-2222-2222-2222-222222222222\",\"created\":true}");
            var session = new ParticipantSession(
                Guid.NewGuid(),
                new ParticipantInput { FirstName = "A", LastName = "B", Title = "Müdür", Company = "Test A.Ş.", Location = "İstanbul / Şişli", Phone = "05321234567", Email = "a@b.co" },
                DateTime.UtcNow);
            session.Gender = Gender.Female;
            session.GenderSelected = true;

            SyncTask.Completed(Client().RegisterAsync(session));

            var body = ParseBody(transport.LastRequest.Body);
            CollectionAssert.AreEquivalent(RegisterParams, Keys(body), "PostgREST resolves the overload by the full key set");
            Assert.AreEqual(JTokenType.Null, body["p_consent_accepted"].Type);
            Assert.AreEqual(JTokenType.Null, body["p_consent_version"].Type);
        }

        [Test]
        public void SubmitBody_HasExactlyTheRpcParameters_WithWireValues()
        {
            transport.EnqueueJson(200, SubmitOkBody);
            var payload = TestData.Payload(score: 15, completionMs: 12345);

            var result = SyncTask.Completed(Client().SubmitResultAsync(payload));

            Assert.IsTrue(result.Ok, result.ToString());
            Assert.AreEqual(4, result.Value.Rank);
            Assert.IsTrue(result.Value.Created);

            var body = ParseBody(transport.LastRequest.Body);
            CollectionAssert.AreEquivalent(SubmitParams, Keys(body));
            Assert.AreEqual(payload.SubmissionId.ToString("D"), (string)body["p_submission_id"]);
            Assert.AreEqual(payload.ClientSessionId.ToString("D"), (string)body["p_client_session_id"]);
            Assert.AreEqual(TestData.EventSlug, (string)body["p_event_slug"]);
            Assert.AreEqual(TestData.AccessCode, (string)body["p_access_code"]);
            Assert.AreEqual(TestData.StationId, (string)body["p_station_id"]);
            Assert.AreEqual(15, (int)body["p_score"]);
            Assert.AreEqual(12345, (long)body["p_completion_ms"]);
            Assert.AreEqual(2, (int)body["p_correct_count"]);
            Assert.AreEqual(1, (int)body["p_incorrect_count"]);
            Assert.AreEqual(2, (int)body["p_required_total"]);
            Assert.AreEqual(JTokenType.Array, body["p_placed_product_ids"].Type);
            CollectionAssert.AreEqual(new[] { "laptop", "beach-towel", "pen" }, body["p_placed_product_ids"].ToObject<string[]>());
            Assert.AreEqual("female", (string)body["p_gender"]);
            Assert.AreEqual("completed", (string)body["p_status"]);
            Assert.AreEqual("required_items_placed", (string)body["p_completion_reason"]);
            Assert.AreEqual("2026-10-01T09:05:30.250Z", (string)body["p_completed_at"]);
            Assert.AreEqual(TestData.ClientVersion, (string)body["p_client_version"]);

            var participant = (JObject)body["p_participant"];
            foreach (var key in ParticipantJsonKeys)
            {
                Assert.IsNotNull(participant.Property(key), "p_participant is missing key " + key);
            }

            Assert.AreEqual(TestData.PiiPhone, (string)participant["phone"]);
            Assert.AreEqual(TestData.PiiEmail, (string)participant["email"]);
            Assert.AreEqual("female", (string)participant["gender"]);
            Assert.AreEqual(TestData.StationId, (string)participant["station_id"]);
        }

        [Test]
        public void LeaderboardAndPingBodies_HaveExactlyTheRpcParameters()
        {
            transport.EnqueueJson(200, "[]");
            transport.EnqueueJson(200, "{\"ok\":true,\"event_name\":\"Test\",\"server_time\":\"2026-10-01T10:00:00+00:00\"}");
            var client = Client();

            SyncTask.Completed(client.GetLeaderboardAsync(25));
            var ping = SyncTask.Completed(client.PingAsync());

            var leaderboardBody = ParseBody(transport.Requests[0].Body);
            CollectionAssert.AreEquivalent(LeaderboardParams, Keys(leaderboardBody));
            Assert.AreEqual(TestData.EventSlug, (string)leaderboardBody["p_event_slug"]);
            Assert.AreEqual(25, (int)leaderboardBody["p_limit"]);

            var pingBody = ParseBody(transport.Requests[1].Body);
            CollectionAssert.AreEquivalent(PingParams, Keys(pingBody));
            Assert.AreEqual(TestData.AccessCode, (string)pingBody["p_access_code"]);

            Assert.IsTrue(ping.Ok, ping.ToString());
            Assert.IsTrue(ping.Value.Ok);
            Assert.AreEqual("Test", ping.Value.EventName);
        }

        [TestCase(0, 100)]
        [TestCase(-3, 100)]
        [TestCase(1, 1)]
        [TestCase(1000, 1000)]
        [TestCase(5000, 1000)]
        public void Leaderboard_LimitIsClampedToServerRange(int requested, int sent)
        {
            transport.EnqueueJson(200, "[]");

            SyncTask.Completed(Client().GetLeaderboardAsync(requested));

            Assert.AreEqual(sent, (int)ParseBody(transport.LastRequest.Body)["p_limit"]);
        }

        [Test]
        public void RequestParameterNames_MatchSqlMigrations()
        {
            var functions = ReadSqlFunctionParameters();
            if (functions == null)
            {
                Assert.Ignore("backend/supabase/migrations not found next to the Unity project; covered by the hard-coded lists.");
            }

            AssertSameParameters(functions, SupabaseBackendClient.RegisterFunction, RegisterParams);
            AssertSameParameters(functions, SupabaseBackendClient.SubmitResultFunction, SubmitParams);
            AssertSameParameters(functions, SupabaseBackendClient.LeaderboardFunction, LeaderboardParams);
            AssertSameParameters(functions, SupabaseBackendClient.PingFunction, PingParams);

            // And the serialised DTOs carry exactly those names.
            var session = TestData.CompletedSession();
            var config = TestData.Config();
            CollectionAssert.AreEquivalent(
                functions[SupabaseBackendClient.RegisterFunction],
                Keys(ParseBody(BackendJson.Serialize(SupabaseBackendClient.BuildRegisterRequest(config, session)))));
            CollectionAssert.AreEquivalent(
                functions[SupabaseBackendClient.SubmitResultFunction],
                Keys(ParseBody(BackendJson.Serialize(SupabaseBackendClient.BuildSubmitRequest(config, TestData.Payload())))));
            CollectionAssert.AreEquivalent(
                functions[SupabaseBackendClient.LeaderboardFunction],
                Keys(ParseBody(BackendJson.Serialize(new GetLeaderboardRequest()))));
            CollectionAssert.AreEquivalent(
                functions[SupabaseBackendClient.PingFunction],
                Keys(ParseBody(BackendJson.Serialize(new PingRequest()))));
        }

        // ----- error mapping -----

        [Test]
        public void EventAccessDenied_MapsToTurkishMessage_NotTransient()
        {
            transport.EnqueueJson(400, "{\"code\":\"P0001\",\"details\":\"access code mismatch\",\"hint\":null,\"message\":\"EVENT_ACCESS_DENIED\"}");

            var error = SubmitExpectingError();

            Assert.AreEqual(BackendError.CodeEventAccessDenied, error.Code);
            Assert.AreEqual(BackendErrorMessages.EventAccessDenied, error.Message);
            StringAssert.StartsWith("Etkinlik erişimi reddedildi", error.Message);
            Assert.AreEqual(400, error.HttpStatus);
            Assert.IsFalse(error.IsTransient);
            StringAssert.Contains("P0001", error.Detail);
            StringAssert.Contains("access code mismatch", error.Detail);
        }

        [Test]
        public void ValidationFailedPhone_MapsToFieldSpecificTurkishMessage()
        {
            transport.EnqueueJson(400, "{\"code\":\"P0001\",\"details\":\"phone must be 10..15 digits\",\"hint\":null,\"message\":\"VALIDATION_FAILED:phone\"}");

            var error = SubmitExpectingError();

            Assert.AreEqual("VALIDATION_FAILED:phone", error.Code);
            Assert.IsTrue(error.IsValidationFailure);
            Assert.AreEqual("phone", error.ValidationField);
            Assert.AreEqual("Geçersiz alan: Telefon", error.Message);
            Assert.IsFalse(error.IsTransient);
        }

        [TestCase("VALIDATION_FAILED:first_name", "Geçersiz alan: Ad")]
        [TestCase("VALIDATION_FAILED:email", "Geçersiz alan: E-posta")]
        [TestCase("VALIDATION_FAILED:completion_ms", "Geçersiz alan: Süre")]
        [TestCase("EVENT_INACTIVE", BackendErrorMessages.EventInactive)]
        [TestCase("PARTICIPANT_NOT_FOUND", BackendErrorMessages.ParticipantNotFound)]
        public void KnownServerCodes_MapToTurkishMessages(string code, string expectedMessage)
        {
            transport.EnqueueJson(400, "{\"code\":\"P0001\",\"details\":null,\"hint\":null,\"message\":\"" + code + "\"}");

            var error = SubmitExpectingError();

            Assert.AreEqual(code, error.Code);
            Assert.AreEqual(expectedMessage, error.Message);
            Assert.IsFalse(error.IsTransient);
        }

        [TestCase(500)]
        [TestCase(502)]
        [TestCase(503)]
        [TestCase(504)]
        [TestCase(408)]
        [TestCase(429)]
        public void ServerAndThrottlingStatuses_AreTransient_ServerUnreachable(int status)
        {
            transport.EnqueueJson(status, "<html><body>upstream error</body></html>");

            var error = SubmitExpectingError();

            Assert.IsTrue(error.IsTransient);
            Assert.AreEqual("Sunucuya ulaşılamadı", error.Message);
            Assert.AreEqual(status, error.HttpStatus);
        }

        [Test]
        public void Timeout_IsTransient_ServerUnreachable()
        {
            transport.EnqueueTimeout();

            var error = SubmitExpectingError();

            Assert.AreEqual(BackendError.CodeTimeout, error.Code);
            Assert.IsTrue(error.IsTransient);
            Assert.AreEqual("Sunucuya ulaşılamadı", error.Message);
        }

        [Test]
        public void NetworkError_IsTransient_ServerUnreachable()
        {
            transport.EnqueueNetworkError();

            var error = SubmitExpectingError();

            Assert.AreEqual(BackendError.CodeTransport, error.Code);
            Assert.AreEqual(0, error.HttpStatus);
            Assert.IsTrue(error.IsTransient);
            Assert.AreEqual("Sunucuya ulaşılamadı", error.Message);
        }

        [Test]
        public void TransportException_IsMappedToTransientError_NotThrown()
        {
            transport.ExceptionToThrow = new IOException("socket closed");

            var error = SubmitExpectingError();

            Assert.AreEqual(BackendError.CodeTransport, error.Code);
            Assert.IsTrue(error.IsTransient);
            Assert.AreEqual("Sunucuya ulaşılamadı", error.Message);
            StringAssert.Contains("socket closed", error.Detail);
        }

        [Test]
        public void TransportCancellation_MapsToCancelled()
        {
            transport.ExceptionToThrow = new OperationCanceledException();

            var error = SubmitExpectingError();

            Assert.AreEqual(BackendError.CodeCancelled, error.Code);
        }

        [Test]
        public void Unauthorized_IsNotTransient()
        {
            transport.EnqueueJson(401, "{\"message\":\"Invalid API key\",\"hint\":\"Double check your Supabase `anon` or `service_role` API key.\"}");

            var error = SubmitExpectingError();

            Assert.AreEqual(BackendErrorMessages.Unauthorized, error.Message);
            StringAssert.StartsWith("Sunucuya ulaşılamadı", error.Message);
            Assert.IsFalse(error.IsTransient);
        }

        [Test]
        public void UnknownPostgrestError_FallsBackToServerUnreachable()
        {
            transport.EnqueueJson(404, "{\"code\":\"PGRST202\",\"details\":null,\"hint\":null,\"message\":\"Could not find the function public.submit_result\"}");

            var error = SubmitExpectingError();

            Assert.AreEqual("Sunucuya ulaşılamadı", error.Message);
            Assert.IsFalse(error.IsTransient);
            StringAssert.Contains("PGRST202", error.Detail);
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("not json")]
        public void SuccessStatusWithUnusableBody_IsInvalidResponse(string body)
        {
            transport.EnqueueJson(200, body);

            var error = SubmitExpectingError();

            Assert.AreEqual(BackendError.CodeInvalidResponse, error.Code);
            Assert.IsFalse(error.IsTransient);
        }

        // ----- configuration -----

        [TestCase("", TestData.AnonKey)]
        [TestCase(TestData.Url, "")]
        [TestCase("", "")]
        public void MissingUrlOrKey_ReturnsConfigurationError_WithoutCallingTransport(string url, string key)
        {
            var client = Client(url: url, anonKey: key);

            var register = SyncTask.Completed(client.RegisterAsync(TestData.CompletedSession()));
            var submit = SyncTask.Completed(client.SubmitResultAsync(TestData.Payload()));
            var leaderboard = SyncTask.Completed(client.GetLeaderboardAsync(10));
            var ping = SyncTask.Completed(client.PingAsync());

            foreach (var error in new[] { register.Error, submit.Error, leaderboard.Error, ping.Error })
            {
                Assert.IsNotNull(error);
                Assert.AreEqual(BackendError.CodeConfigurationMissing, error.Code);
                Assert.AreEqual(BackendErrorMessages.ConfigurationMissing, error.Message);
                Assert.IsFalse(error.IsTransient);
            }

            Assert.AreEqual(0, transport.Requests.Count);
        }

        [Test]
        public void MissingAccessCode_BlocksAuthenticatedCalls_ButNotLeaderboard()
        {
            transport.EnqueueJson(200, "[]");
            var client = Client(accessCode: "");

            Assert.AreEqual(BackendError.CodeConfigurationMissing, SyncTask.Completed(client.SubmitResultAsync(TestData.Payload())).Error.Code);
            Assert.AreEqual(BackendError.CodeConfigurationMissing, SyncTask.Completed(client.RegisterAsync(TestData.CompletedSession())).Error.Code);
            Assert.AreEqual(BackendError.CodeConfigurationMissing, SyncTask.Completed(client.PingAsync()).Error.Code);
            Assert.AreEqual(0, transport.Requests.Count);

            var leaderboard = SyncTask.Completed(client.GetLeaderboardAsync(10));
            Assert.IsTrue(leaderboard.Ok, leaderboard.ToString());
            Assert.AreEqual(1, transport.Requests.Count);
        }

        [Test]
        public void Register_BeforeGenderSelected_FailsWithoutCallingTransport()
        {
            var session = new ParticipantSession(Guid.NewGuid(), TestData.ValidInput(), DateTime.UtcNow);

            var result = SyncTask.Completed(Client().RegisterAsync(session));

            Assert.IsFalse(result.Ok);
            Assert.AreEqual("gender", result.Error.ValidationField);
            Assert.AreEqual(0, transport.Requests.Count);
        }

        // ----- leaderboard parsing -----

        [Test]
        public void Leaderboard_ParsesRows()
        {
            transport.EnqueueJson(200,
                "[{\"rank\":1,\"display_name\":\"Ayşe Y.\",\"score\":30,\"completion_ms\":20000,\"gender\":\"female\",\"completed_at\":\"2026-10-01T10:00:00.123456+00:00\"}," +
                "{\"rank\":2,\"display_name\":\"Mehmet Kaya\",\"score\":30,\"completion_ms\":25000,\"gender\":\"male\",\"completed_at\":\"2026-10-01T13:05:00+03:00\"}]");

            var result = SyncTask.Completed(Client().GetLeaderboardAsync(10));

            Assert.IsTrue(result.Ok, result.ToString());
            var rows = result.Value;
            Assert.AreEqual(2, rows.Length);

            Assert.AreEqual(1, rows[0].Rank);
            Assert.AreEqual("Ayşe Y.", rows[0].DisplayName);
            Assert.AreEqual(30, rows[0].Score);
            Assert.AreEqual(20000, rows[0].CompletionMs);
            Assert.AreEqual("female", rows[0].Gender);
            Assert.IsTrue(rows[0].CompletedAt.HasValue);
            var expectedFirst = new DateTime(2026, 10, 1, 10, 0, 0, 123, DateTimeKind.Utc);
            Assert.Less(Math.Abs((rows[0].CompletedAt.Value.ToUniversalTime() - expectedFirst).TotalMilliseconds), 1d);

            Assert.AreEqual(2, rows[1].Rank);
            Assert.AreEqual("Mehmet Kaya", rows[1].DisplayName);
            Assert.AreEqual(new DateTime(2026, 10, 1, 10, 5, 0, DateTimeKind.Utc), rows[1].CompletedAt.Value.ToUniversalTime());
        }

        [Test]
        public void Leaderboard_EmptyArray_IsOkAndEmpty()
        {
            transport.EnqueueJson(200, "[]");

            var result = SyncTask.Completed(Client().GetLeaderboardAsync(10));

            Assert.IsTrue(result.Ok);
            Assert.AreEqual(0, result.Value.Length);
        }

        // ----- helpers -----

        private static void AssertSameParameters(Dictionary<string, string[]> functions, string function, string[] expected)
        {
            Assert.IsTrue(functions.ContainsKey(function), "SQL migrations do not define public." + function);
            CollectionAssert.AreEquivalent(expected, functions[function], "parameters of public." + function);
        }

        /// <summary>Function name → parameter names, from the last migration defining each function; null when the folder is absent.</summary>
        private static Dictionary<string, string[]> ReadSqlFunctionParameters()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var migrations = Path.GetFullPath(Path.Combine(projectRoot, "..", "backend", "supabase", "migrations"));
            if (!Directory.Exists(migrations))
            {
                return null;
            }

            var files = Directory.GetFiles(migrations, "*.sql");
            if (files.Length == 0)
            {
                return null;
            }

            Array.Sort(files, StringComparer.Ordinal);
            var pattern = new Regex(@"create\s+or\s+replace\s+function\s+public\.(\w+)\s*\(([^)]*)\)", RegexOptions.IgnoreCase);
            var comment = new Regex(@"--[^\n]*");
            var result = new Dictionary<string, string[]>(StringComparer.Ordinal);

            foreach (var file in files)
            {
                var sql = comment.Replace(File.ReadAllText(file), string.Empty);
                foreach (Match match in pattern.Matches(sql))
                {
                    var names = new List<string>();
                    foreach (var part in match.Groups[2].Value.Split(','))
                    {
                        var trimmed = part.Trim();
                        if (trimmed.Length == 0)
                        {
                            continue;
                        }

                        names.Add(trimmed.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0]);
                    }

                    result[match.Groups[1].Value] = names.ToArray();
                }
            }

            return result;
        }
    }
}
