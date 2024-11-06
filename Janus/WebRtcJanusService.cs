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
        private static readonly string LogHeader = "[WEBRTC JANUS SERVICE]";

        private readonly IConfigSource _Config;
        private bool _Enabled = false;

        private string _JanusServerURI = String.Empty;
        private string _JanusAPIToken = String.Empty;
        private string _JanusAdminURI = String.Empty;
        private string _JanusAdminToken = String.Empty;

        private JanusSession _JanusSession;
        private JanusAudioBridge _AudioBridge;

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
        private void StartConnectionToJanus()
        {
            _log.DebugFormat("{0} StartConnectionToJanus", LogHeader);
            Task.Run(async () => 
            {
                JanusComm _JanusComm = new JanusComm(_JanusServerURI, _JanusAPIToken, _JanusAdminURI, _JanusAdminToken);
                if (_JanusComm is not null)
                {
                    _log.DebugFormat("{0} JanusComm created", LogHeader);
                    _JanusSession = new JanusSession(_JanusComm);
                    if (await _JanusSession.CreateSession())
                    {
                        _log.DebugFormat("{0} JanusSession created", LogHeader);
                        // Once the session is created, create a handle to the plugin for rooms

                        _AudioBridge = new JanusAudioBridge(_JanusSession);
                        if (await _AudioBridge.Activate(_Config))
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
                else
                {
                    _log.ErrorFormat("{0} JanusComm not created", LogHeader);

                }

            });
        }

        // The pRequest parameter is a straight conversion of the JSON request from the client.
        // This is the logic that takes the client's request and converts it into
        //     operations on rooms in the audio bridge.
        // IWebRtcVoiceService.ProvisionVoiceAccountRequest
        public async Task<OSDMap> ProvisionVoiceAccountRequest(OSDMap pRequest, UUID pUserID, IScene pScene)
        {
            OSDMap ret = null;
            if (_AudioBridge is not null)
            {
                // TODO: check for logout=true
                // need to keep count of users in a room to know when to close a room

                // Get the parameters that select the room
                // To get here, voice_server_type has already been checked to be 'webrtc' and channel_type='local'
                int parcel_local_id = pRequest.ContainsKey("parcel_id") ? pRequest["parcel_id"].AsInteger() : -999;
                string channel_type = pRequest["channel_type"].AsString();
                string voice_server_type = pRequest["voice_server_type"].AsString();

                if (pRequest.ContainsKey("jsep") && pRequest["jsep"] is OSDMap jsep)
                {
                    // The jsep is the SDP from the client. This is the client's request to connect to the audio bridge.
                    string jsepType = jsep["type"].AsString();
                    string jsepSdp = jsep["sdp"].AsString();
                    if (jsepType == "offer")
                    {
                        _log.DebugFormat("{0} ProvisionVoiceAccountRequest: jsep type={1} sdp={2}", LogHeader, jsepType, jsepSdp);
                        // The audio bridge will create a room for the client and return the room ID
                        JanusRoom room = await AllocateRoom(pUserID, parcel_local_id);

                    }
                    else
                    {
                        _log.ErrorFormat("{0} ProvisionVoiceAccountRequest: jsep type={1} not offer", LogHeader, jsepType);
                    }
                }
                else
                {
                    _log.DebugFormat("{0} ProvisionVoiceAccountRequest: no jsep", LogHeader);
                }
            }
            else
            {
                _log.ErrorFormat("{0} ProvisionVoiceAccountRequest: no JanusAudioBridge", LogHeader);
            }
            return ret;
        }

        // IWebRtcVoiceService.VoiceAccountBalanceRequest
        public Task<OSDMap> VoiceSignalingRequest(OSDMap pRequest, UUID pUserID, IScene pScene)
        {
            throw new System.NotImplementedException();
        }

        private async Task<JanusRoom> AllocateRoom(UUID pUserID, int pParcelLocalID)
        {
            JanusRoom ret = null;
            if (_AudioBridge is not null)
            {
                // The audio bridge will create a room for the client and return the room ID
                // ret = await _AudioBridge.Create(pUserID, pParcelLocalID);
            }
            return ret;
        }
    }
 }
