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
using System.Reflection;

using OpenSim.Framework;
using OpenSim.Services.Interfaces;
using OpenSim.Services.Base;

using OpenMetaverse.StructuredData;
using OpenMetaverse;

using Nini.Config;
using log4net;
using System.Threading.Tasks;

namespace WebRtcVoice
{
    // Encapsulization of a Session to the Janus server
    public class JanusSession : IDisposable
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly string LogHeader = "[JANUS SESSION]";

        private JanusComm _JanusComm;

        public string SessionId { get; private set; }
        public string SessionUri { get ; private set ; }

        public bool IsConnected => !String.IsNullOrEmpty(SessionId);

        // Wrapper around the session connection to Janus-gateway
        public JanusSession(JanusComm pComm)
        {
            m_log.DebugFormat("{0} JanusSession constructor", LogHeader);
            _JanusComm = pComm;
        }

        public void Dispose()
        {
            if (IsConnected)
            {
                // Close the session

            }
        }

        // Send a request to the Janus server within the session.
        public Task<JanusMessageResp> PostToJanus(JanusMessageReq pReq)
        {
            return _JanusComm.PostToJanus(pReq, SessionUri);
        }

        public Task<JanusMessageResp> PostToJanus(JanusMessageReq pReq, string pUri)
        {
            return _JanusComm.PostToJanus(pReq, pUri);
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
                var resp = await _JanusComm.PostToJanus(new CreateSessionReq());
                if (resp is not null && resp.isSuccess)
                {
                    var sessionResp = new CreateSessionResp(resp);
                    SessionId = sessionResp.sessionId;
                    SessionUri = _JanusComm.JanusServerURI + "/" + SessionId;
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
                        var resp = await _JanusComm.GetFromJanus(SessionUri);
                        if (resp is not null)
                        {
                            switch (resp.ReturnCode)
                            {
                                case "event":
                                    m_log.DebugFormat("{0} EventLongPoll: event {1}", LogHeader, resp.ToString());
                                    // TODO: call event handler
                                    break;
                                case "webrtcup":
                                    //  ICE and DTLS succeeded, and so Janus correctly established a PeerConnection with the user/application;
                                    m_log.DebugFormat("{0} EventLongPoll: error {1}", LogHeader, resp.ToString());
                                    break;
                                case "media":
                                    // Janus is receiving (receiving: true/false) audio/video (type: "audio/video") on this PeerConnection;
                                    m_log.DebugFormat("{0} EventLongPoll: error {1}", LogHeader, resp.ToString());
                                    break;
                                case "slowlink":
                                    // Janus detected a slowlink (uplink: true/false) on this PeerConnection;
                                    m_log.DebugFormat("{0} EventLongPoll: error {1}", LogHeader, resp.ToString());
                                    break;
                                case "hangup":
                                    // The PeerConnection was closed, either by the user/application or by Janus itself;
                                    m_log.DebugFormat("{0} EventLongPoll: error {1}", LogHeader, resp.ToString());
                                    break;
                                case "keepalive":
                                    // These should happen every 30 seconds
                                    m_log.DebugFormat("{0} EventLongPoll: keepalive {1}", LogHeader, resp.ToString());
                                    break;
                                case "error":
                                    m_log.DebugFormat("{0} EventLongPoll: error {1}", LogHeader, resp.ToString());
                                    break;
                                default:
                                    m_log.DebugFormat("{0} EventLongPoll: unknown response {1}", LogHeader, resp.ToString());
                                    break;
                            }
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