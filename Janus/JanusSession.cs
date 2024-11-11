// Copyright 2024 Robert Adams (misterblue@misterblue.com)
//
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Mime;
using System.Reflection;
using System.Threading.Tasks;

using OpenSim.Framework;
using OpenSim.Services.Interfaces;
using OpenSim.Services.Base;

using OpenMetaverse.StructuredData;
using OpenMetaverse;

using Nini.Config;
using log4net;

namespace WebRtcVoice
{
    // Encapsulization of a Session to the Janus server
    public class JanusSession : IDisposable
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly string LogHeader = "[JANUS SESSION]";

        private string _JanusServerURI = String.Empty;
        private string _JanusAPIToken = String.Empty;
        private string _JanusAdminURI = String.Empty;
        private string _JanusAdminToken = String.Empty;

        public string JanusServerURI => _JanusServerURI;
        public string JanusAdminURI => _JanusAdminURI;

        public string SessionId { get; private set; }
        public string SessionUri { get ; private set ; }

        private HttpClient _HttpClient = new HttpClient();

        public bool IsConnected => !String.IsNullOrEmpty(SessionId);

        // Wrapper around the session connection to Janus-gateway
        public JanusSession(string pServerURI, string pAPIToken, string pAdminURI, string pAdminToken)
        {
            m_log.DebugFormat("{0} JanusSession constructor", LogHeader);
            _JanusServerURI = pServerURI;
            _JanusAPIToken = pAPIToken;
            _JanusAdminURI = pAdminURI;
            _JanusAdminToken = pAdminToken;
        }

        public void Dispose()
        {
            if (_HttpClient is not null)
            {
                _HttpClient.Dispose();
                _HttpClient = null;
            }
            if (IsConnected)
            {
                // Close the session

            }
        }

        /// <summary>
        /// Make the create session request to the Janus server, get the
        /// sessionID and return TRUE if successful.
        /// </summary>
        /// <returns>TRUE if session was created successfully</returns>
        public async Task<bool> CreateSession()
        {
            bool ret = false;
            try
            {
                var resp = await PostToJanus(new CreateSessionReq());
                if (resp is not null && resp.isSuccess)
                {
                    var sessionResp = new CreateSessionResp(resp);
                    SessionId = sessionResp.sessionId;
                    SessionUri = _JanusServerURI + "/" + SessionId;
                    m_log.DebugFormat("{0} CreateSession. Created. ID={1}, URL={2}", LogHeader, SessionId, SessionUri);
                    ret = true;
                    StartLongPoll();
                }
                else
                {
                    m_log.ErrorFormat("{0} CreateSession: failed", LogHeader);
                }
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("{0} CreateSession: exception {1}", LogHeader, e);
            }

            return ret;
        }

        // ====================================================================
        public Dictionary<string, JanusPlugin> _Plugins = new Dictionary<string, JanusPlugin>();
        public void AddPlugin(JanusPlugin pPlugin)
        {
            _Plugins.Add(pPlugin.PluginName, pPlugin);
        }
        // ====================================================================
        // Post to the session
        public async Task<JanusMessageResp> PostToSession(JanusMessageReq pReq)
        {
            return await PostToJanus(pReq, SessionUri);
        }

        private class OutstandingRequest
        {
            public string TransactionId;
            public DateTime RequestTime;
            public TaskCompletionSource<JanusMessageResp> TaskCompletionSource;
        }
        private Dictionary<string, OutstandingRequest> _OutstandingRequests = new Dictionary<string, OutstandingRequest>();

        // Send a request to the Janus server within the session.
        // NOTE: this is probably NOT what you want to do. This is a direct call that is outside the session.
        private async Task<JanusMessageResp> PostToJanus(JanusMessageReq pReq)
        {
            return await PostToJanus(pReq, _JanusServerURI);
        }

        private async Task<JanusMessageResp> PostToJanus(JanusMessageReq pReq, string pURI)
        {
            if (!String.IsNullOrEmpty(_JanusAPIToken))
            {
                pReq.AddAPIToken(_JanusAPIToken);
            }
            if (String.IsNullOrEmpty(pReq.TransactionId))
            {
                pReq.TransactionId = Guid.NewGuid().ToString();
            }
            // m_log.DebugFormat("{0} PostToJanus", LogHeader);
            m_log.DebugFormat("{0} PostToJanus. URI={1}, req={2}", LogHeader, pURI, pReq.ToJson());

            JanusMessageResp ret = null;
            try
            {
                OutstandingRequest outReq = new OutstandingRequest
                {
                    TransactionId = pReq.TransactionId,
                    RequestTime = DateTime.Now,
                    TaskCompletionSource = new TaskCompletionSource<JanusMessageResp>()
                };
                _OutstandingRequests.Add(pReq.TransactionId, outReq);

                HttpRequestMessage reqMsg = new HttpRequestMessage(HttpMethod.Post, pURI);
                string reqStr = pReq.ToJson();
                reqMsg.Content = new StringContent(reqStr, System.Text.Encoding.UTF8, MediaTypeNames.Application.Json);
                reqMsg.Headers.Add("Accept", "application/json");
                HttpResponseMessage response = await _HttpClient.SendAsync(reqMsg);

                if (response.IsSuccessStatusCode)
                {
                    string respStr = await response.Content.ReadAsStringAsync();
                    ret = JanusMessageResp.FromJson(respStr);
                    if (ret.CheckReturnCode("ack"))
                    {
                        // Some messages are asynchronous and completed with an event
                        m_log.DebugFormat("{0} PostToJanus: ack response {1}", LogHeader, respStr);
                        ret = await _OutstandingRequests[pReq.TransactionId].TaskCompletionSource.Task;
                        _OutstandingRequests.Remove(pReq.TransactionId);

                    }
                    else 
                    {
                        // If the response is not an ack, that means a synchronous request/response so return the response
                        _OutstandingRequests.Remove(pReq.TransactionId);
                        m_log.DebugFormat("{0} PostToJanus: response {1}", LogHeader, respStr);
                    }
                }
                else
                {
                    m_log.ErrorFormat("{0} PostToJanus: response not successful {1}", LogHeader, response);
                    _OutstandingRequests.Remove(pReq.TransactionId);
                }
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("{0} PostToJanus: exception {1}", LogHeader, e);
            }

            return ret;
        }

        public Task<JanusMessageResp> PostToJanusAdmin(JanusMessageReq pReq)
        {
            return PostToJanus(pReq, _JanusAdminURI);
        }

        public Task<JanusMessageResp> GetFromJanus()
        {
            return GetFromJanus(_JanusServerURI);
        }
        public async Task<JanusMessageResp> GetFromJanus(string pURI)
        {
            if (!String.IsNullOrEmpty(_JanusAPIToken))
            {
                pURI += "?apisecret=" + _JanusAPIToken;
            }

            JanusMessageResp ret = null;
            try
            {
                // m_log.DebugFormat("{0} GetFromJanus: URI = \"{1}\"", LogHeader, pURI);
                HttpRequestMessage reqMsg = new HttpRequestMessage(HttpMethod.Get, pURI);
                reqMsg.Headers.Add("Accept", "application/json");
                HttpResponseMessage response = await _HttpClient.SendAsync(reqMsg);

                if (response.IsSuccessStatusCode)
                {
                    string respStr = await response.Content.ReadAsStringAsync();
                    ret = JanusMessageResp.FromJson(respStr);
                    // m_log.DebugFormat("{0} GetFromJanus: response {1}", LogHeader, respStr);
                }
                else
                {
                    m_log.ErrorFormat("{0} GetFromJanus: response not successful {1}", LogHeader, response);
                }
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("{0} GetFromJanus: exception {1}", LogHeader, e);
            }

            return ret;
        }

        // ====================================================================
        /// <summary>
        /// In the REST API, events are returned by a long poll. This
        /// starts the poll and calls the registed event handler when
        /// an event is received.
        /// </summary>
        private void StartLongPoll()
        {
            m_log.DebugFormat("{0} EventLongPoll", LogHeader);
            Task.Run(async () => {
                while (IsConnected)
                {
                    try
                    {
                        var resp = await GetFromJanus(SessionUri);
                        if (resp is not null)
                        {
                            _ = Task.Run(() =>
                            {
                                switch (resp.ReturnCode)
                                {
                                    case "keepalive":
                                        // These should happen every 30 seconds
                                        m_log.DebugFormat("{0} EventLongPoll: keepalive {1}", LogHeader, resp.ToString());
                                        break;
                                    case "server_info":
                                        // Just info on the Janus instance
                                        m_log.DebugFormat("{0} EventLongPoll: server_info {1}", LogHeader, resp.ToString());
                                        break;
                                    case "ack":
                                        // 'ack' says the request was received and an event will follow
                                        m_log.DebugFormat("{0} EventLongPoll: ack {1}", LogHeader, resp.ToString());
                                        break;
                                    case "success":
                                        // success is a sync response that says the request was completed
                                        m_log.DebugFormat("{0} EventLongPoll: success {1}", LogHeader, resp.ToString());
                                        break;
                                    case "trickle":
                                        // got a trickle ICE candidate from Janus
                                        // this is for reverse communication from Janus to the client and we don't do that
                                        m_log.DebugFormat("{0} EventLongPoll: trickle {1}", LogHeader, resp.ToString());
                                        break;
                                    case "webrtcup":
                                        //  ICE and DTLS succeeded, and so Janus correctly established a PeerConnection with the user/application;
                                        m_log.DebugFormat("{0} EventLongPoll: webrtcup {1}", LogHeader, resp.ToString());
                                        // TODO: if (TryGetPluginHandle(resp, out pluginHandle))
                                        // TODO: pluginHandle.webrtcState(true);
                                        break;
                                    case "hangup":
                                        // The PeerConnection was closed, either by the user/application or by Janus itself;
                                        m_log.DebugFormat("{0} EventLongPoll: hangup {1}", LogHeader, resp.ToString());
                                        // TODO: if (TryGetPluginHandle(resp, out pluginHandle))
                                        // TODO: pluginHandle.webrtcState(false);
                                        // TODO: pluginHandle.hangup();
                                        break;
                                    case "detached":
                                        // a plugin asked the core to detach one of our handles
                                        m_log.DebugFormat("{0} EventLongPoll: event {1}", LogHeader, resp.ToString());
                                        // TODO: if (TryGetPluginHandle(resp, out pluginHandle))
                                        // TODO: pluginHandle.detach();
                                        break;
                                    case "media":
                                        // Janus is receiving (receiving: true/false) audio/video (type: "audio/video") on this PeerConnection;
                                        m_log.DebugFormat("{0} EventLongPoll: media {1}", LogHeader, resp.ToString());
                                        // TODO: if (TryGetPluginHandle(resp, out pluginHandle))
                                        // TODO: pluginHandle.mediaState(resp.type, resp.receiving, resp.mid);
                                        break;
                                    case "slowlink":
                                        // Janus detected a slowlink (uplink: true/false) on this PeerConnection;
                                        m_log.DebugFormat("{0} EventLongPoll: slowlink {1}", LogHeader, resp.ToString());
                                        // TODO: if (TryGetPluginHandle(resp, out pluginHandle))
                                        // TODO: pluginHandle.mediaState(resp.uplink, resp.lost, resp.mid);
                                        break;
                                    case "error":
                                        m_log.DebugFormat("{0} EventLongPoll: error {1}", LogHeader, resp.ToString());
                                        // TODO: SendResponseToOutStandingRequest(resp);
                                        // or
                                        // TODO: if (TryGetOutStandingRequest(resp, out outstandingRequetransaction
                                        // TODO: outstandingRequest.TaskCompletionSource.SetResult(resp);
                                        break;
                                    case "event":
                                        m_log.DebugFormat("{0} EventLongPoll: event {1}", LogHeader, resp.ToString());
                                        // TODO: if (TryGetPluginHandle(resp, out pluginHandle))
                                        // TODO: pluginHandle.event(resp.data, resp.jsep);
                                        break;
                                    case "timeout":
                                        // Events for the audio bridge
                                        m_log.DebugFormat("{0} EventLongPoll: timeout {1}", LogHeader, resp.ToString());
                                        break;

                                    case "joined":
                                        // Events for the audio bridge
                                        m_log.DebugFormat("{0} EventLongPoll: joined {1}", LogHeader, resp.ToString());
                                        break;
                                    case "leaving":
                                        // Events for the audio bridge
                                        m_log.DebugFormat("{0} EventLongPoll: leaving {1}", LogHeader, resp.ToString());
                                        break;
                                    default:
                                        m_log.DebugFormat("{0} EventLongPoll: unknown response {1}", LogHeader, resp.ToString());
                                        break;
                                }
                            });
                        }
                        else
                        {
                            m_log.ErrorFormat("{0} EventLongPoll: failed {1}", LogHeader, resp.ToString());
                        }
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat("{0} EventLongPoll: exception {1}", LogHeader, e);
                    }
                }
                m_log.InfoFormat("{0} EventLongPoll: Exiting long poll loop", LogHeader);
            });
        }
    }
}