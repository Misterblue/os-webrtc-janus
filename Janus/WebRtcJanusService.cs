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
using System.Threading.Tasks;

using OpenSim.Framework;
using OpenSim.Services.Base;

using OpenMetaverse.StructuredData;
using OpenMetaverse;

using Nini.Config;
using log4net;

namespace WebRtcVoice
{
    public class WebRtcJanusService : ServiceBase, IWebRtcVoiceService
    {
        private static readonly ILog _log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly string LogHeader = "[JANUS WEBRTC SERVICE]";

        private readonly IConfigSource _Config;
        private bool _Enabled = false;

        private string _JanusServerURI = String.Empty;
        private string _JanusAPIToken = String.Empty;
        private string _JanusAdminURI = String.Empty;
        private string _JanusAdminToken = String.Empty;

        // An extra "viewer session" that is created initially. Used to verify the service
        //     is working and for a handle for the console commands.
        private JanusViewerSession _ViewerSession;

        public WebRtcJanusService(IConfigSource pConfig) : base(pConfig)
        {
            _log.DebugFormat("{0} WebRtcJanusService constructor", LogHeader);
            _Config = pConfig;
            IConfig webRtcVoiceConfig = _Config.Configs["WebRtcVoice"];

            if (webRtcVoiceConfig is not null)
            {
                _Enabled = webRtcVoiceConfig.GetBoolean("Enabled", false);
                IConfig janusConfig = _Config.Configs["JanusWebRtcVoice"];
                if (_Enabled && janusConfig is not null)
                {
                    _JanusServerURI = janusConfig.GetString("JanusGatewayURI", String.Empty);
                    _JanusAPIToken = janusConfig.GetString("APIToken", String.Empty);
                    _JanusAdminURI = janusConfig.GetString("JanusGatewayAdminURI", String.Empty);
                    _JanusAdminToken = janusConfig.GetString("AdminAPIToken", String.Empty);

                    if (String.IsNullOrEmpty(_JanusServerURI) || String.IsNullOrEmpty(_JanusAPIToken) ||
                        String.IsNullOrEmpty(_JanusAdminURI) || String.IsNullOrEmpty(_JanusAdminToken))
                    {
                        _log.ErrorFormat("{0} JanusWebRtcVoice configuration section missing required fields", LogHeader);
                        _Enabled = false;
                    }

                    if (_Enabled)
                    {
                        _log.DebugFormat("{0} Enabled", LogHeader);
                        StartConnectionToJanus();
                    }
                }
                else
                {
                    _log.ErrorFormat("{0} No JanusWebRtcVoice configuration section", LogHeader);
                    _Enabled = false;
                }
            }
            else
            {
                _log.ErrorFormat("{0} No WebRtcVoice configuration section", LogHeader);
                _Enabled = false;
            }
        }

        // Start a thread to do the connection to the Janus server.
        // Here an initial session is created and then a handle to the audio bridge plugin
        //    is created for the console commands. Since webrtc PeerConnections that are created
        //    my Janus are per-session, the other sessions will be created by the viewer requests.
        private void StartConnectionToJanus()
        {
            _log.DebugFormat("{0} StartConnectionToJanus", LogHeader);
            Task.Run(async () => 
            {
                _ViewerSession = new JanusViewerSession(this);
                await ConnectToSessionAndAudioBridge(_ViewerSession);
            });
        }

        private async Task ConnectToSessionAndAudioBridge(JanusViewerSession pViewerSession)
        {
            JanusSession janusSession = new JanusSession(_JanusServerURI, _JanusAPIToken, _JanusAdminURI, _JanusAdminToken);
            if (await janusSession.CreateSession())
            {
                _log.DebugFormat("{0} JanusSession created", LogHeader);
                // Once the session is created, create a handle to the plugin for rooms

                JanusAudioBridge audioBridge = new JanusAudioBridge(janusSession);
                janusSession.AddPlugin(audioBridge);

                pViewerSession.Session = janusSession;
                pViewerSession.AudioBridge = audioBridge;

                if (await audioBridge.Activate(_Config))
                {
                    _log.DebugFormat("{0} AudioBridgePluginHandle created", LogHeader);
                    // Requests through the capabilities will create rooms
                }
                else
                {
                    _log.ErrorFormat("{0} JanusPluginHandle not created", LogHeader);
                }
            }
            else
            {
                _log.ErrorFormat("{0} JanusSession not created", LogHeader);
            }   
        }

        // The pRequest parameter is a straight conversion of the JSON request from the client.
        // This is the logic that takes the client's request and converts it into
        //     operations on rooms in the audio bridge.
        // IWebRtcVoiceService.ProvisionVoiceAccountRequest
        public async Task<OSDMap> ProvisionVoiceAccountRequest(IVoiceViewerSession pSession, OSDMap pRequest, UUID pUserID, IScene pScene)
        {
            OSDMap ret = null;
            string errorMsg = null;
            JanusViewerSession viewerSession = pSession as JanusViewerSession;
            if (viewerSession is not null)
            {
                if (viewerSession.Session is null)
                {
                    // This is a new session so we must create a new session and handle to the audio bridge
                    await ConnectToSessionAndAudioBridge(viewerSession);
                }

                // TODO: need to keep count of users in a room to know when to close a room
                bool isLogout = pRequest.ContainsKey("logout") && pRequest["logout"].AsBoolean();
                if (isLogout)
                {
                    // The client is logging out. Exit the room.
                    if (viewerSession.Room is not null)
                    {
                        await viewerSession.Room.LeaveRoom(viewerSession);
                        viewerSession.Room = null;
                        return new OSDMap
                        {
                            { "response", "closed" }
                        };
                    }
                }

                // Get the parameters that select the room
                // To get here, voice_server_type has already been checked to be 'webrtc' and channel_type='local'
                int parcel_local_id = pRequest.ContainsKey("parcel_id") ? pRequest["parcel_id"].AsInteger() : JanusAudioBridge.REGION_ROOM_ID;
                string channel_id = pRequest.ContainsKey("channel_id") ? pRequest["channel_id"].AsString() : String.Empty;
                string channel_credentials = pRequest.ContainsKey("credentials") ? pRequest["credentials"].AsString() : String.Empty;
                string channel_type = pRequest["channel_type"].AsString();
                bool isSpacial = channel_type == "local";
                string voice_server_type = pRequest["voice_server_type"].AsString();

                _log.DebugFormat("{0} ProvisionVoiceAccountRequest: parcel_id={1} channel_id={2} channel_type={3} voice_server_type={4}", LogHeader, parcel_local_id, channel_id, channel_type, voice_server_type); 

                if (pRequest.ContainsKey("jsep") && pRequest["jsep"] is OSDMap jsep)
                {
                    // The jsep is the SDP from the client. This is the client's request to connect to the audio bridge.
                    string jsepType = jsep["type"].AsString();
                    string jsepSdp = jsep["sdp"].AsString();
                    if (jsepType == "offer")
                    {
                        _log.DebugFormat("{0} ProvisionVoiceAccountRequest: jsep type={1} sdp={2}", LogHeader, jsepType, jsepSdp);
                        viewerSession.Room = await viewerSession.AudioBridge.SelectRoom(channel_type, isSpacial, parcel_local_id, channel_id);
                        if (viewerSession.Room is null)
                        {
                            errorMsg = "room selection failed";
                            _log.ErrorFormat("{0} ProvisionVoiceAccountRequest: room selection failed", LogHeader);
                        }
                        else {
                            viewerSession.Offer = jsepSdp;
                            viewerSession.OfferOrig = jsepSdp;
                            viewerSession.AgentId = pUserID.ToString();
                            if (await viewerSession.Room.JoinRoom(viewerSession))    
                            {
                                ret = new OSDMap
                                {
                                    { "jsep", viewerSession.Answer },
                                    { "viewer_session", viewerSession.ViewerSessionID }
                                };
                            }
                            else
                            {
                                errorMsg = "JoinRoom failed";
                                _log.ErrorFormat("{0} ProvisionVoiceAccountRequest: JoinRoom failed", LogHeader);
                            }
                        }
                    }
                    else
                    {
                        errorMsg = "jsep type not offer";
                        _log.ErrorFormat("{0} ProvisionVoiceAccountRequest: jsep type={1} not offer", LogHeader, jsepType);
                    }
                }
                else
                {
                    errorMsg = "no jsep";
                    _log.DebugFormat("{0} ProvisionVoiceAccountRequest: no jsep. req={1}", LogHeader, pRequest.ToString());
                }
            }
            else
            {
                errorMsg = "no JanusAudioBridge";
                _log.ErrorFormat("{0} ProvisionVoiceAccountRequest: no JanusAudioBridge", LogHeader);
            }

            if (!String.IsNullOrEmpty(errorMsg) && ret is null)
            {
                // The provision failed so build an error messgage to return
                ret = new OSDMap
                {
                    { "response", "failed" },
                    { "error", errorMsg }
                };
            }

            return ret;
        }

        // IWebRtcVoiceService.VoiceAccountBalanceRequest
        public async Task<OSDMap> VoiceSignalingRequest(IVoiceViewerSession pSession, OSDMap pRequest, UUID pUserID, IScene pScene)
        {
            OSDMap ret = null;
            JanusViewerSession viewerSession = pSession as JanusViewerSession;
            if (viewerSession is not null)
            {
                // The request should be an array of candidates
                if (pRequest.ContainsKey("candidate") && pRequest["candidate"] is OSDMap completed)
                {
                    if (completed.ContainsKey("completed") && completed["completed"].AsBoolean())
                    {
                        // The client has finished sending candidates
                        // var candiateResp = await viewerSession.Session.PostToSession(new TrickleReq(viewerSession));
                        var candiateResp =
                            await viewerSession.Session.PostToJanus(new TrickleReq(viewerSession), viewerSession.AudioBridge.PluginUri);
                        _log.DebugFormat("{0} VoiceSignalingRequest: candidate completed", LogHeader);
                    }
                }
                else
                {
                    if (pRequest.ContainsKey("candidates") && pRequest["candidates"] is OSDArray candidates)
                    {
                        OSDArray candidatesArray = new OSDArray();
                        foreach (OSDMap candidate in candidates)
                        {
                            candidatesArray.Add(new OSDMap() {
                                { "candidate", candidate["candidate"].AsString() },
                                { "sdpMid", candidate["sdpMid"].AsString() },
                                { "sdpMLineIndex", candidate["sdpMLineIndex"].AsLong() }
                            });
                        }
                        // var candidatesResp = await viewerSession.Session.PostToSession(new TrickleReq(viewerSession, candidatesArray));
                        var candiateResp =
                            await viewerSession.Session.PostToJanus(new TrickleReq(viewerSession), viewerSession.AudioBridge.PluginUri);
                    }
                    else
                    {
                        _log.ErrorFormat("{0} VoiceSignalingRequest: no candidates", LogHeader);
                    }
                }
            }
            return ret;
        }

        public Task<OSDMap> ProvisionVoiceAccountRequest(OSDMap pRequest, UUID pUserID, IScene pScene)
        {
            throw new NotImplementedException();
        }

        public Task<OSDMap> VoiceSignalingRequest(OSDMap pRequest, UUID pUserID, IScene pScene)
        {
            throw new NotImplementedException();
        }

        // The viewer session object holds all the connection information to Janus.
        public IVoiceViewerSession CreateViewerSession(OSDMap pRequest, UUID pUserID, IScene pScene)
        {
            return new JanusViewerSession(this);
        }
    }
 }
