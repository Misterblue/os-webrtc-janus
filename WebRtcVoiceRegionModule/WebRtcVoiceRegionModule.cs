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
using System.IO;
using System.Net;
using System.Text;
using System.Collections.Generic;
using System.Reflection;

using Mono.Addins;

using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using Caps = OpenSim.Framework.Capabilities.Caps;

using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OSDMap = OpenMetaverse.StructuredData.OSDMap;

using log4net;
using Nini.Config;

[assembly: Addin("WebRtcVoiceRegionModule", "1.0")]
[assembly: AddinDependency("OpenSim.Region.Framework", OpenSim.VersionInfo.VersionNumber)]

namespace WebRtcVoice
{
    /// <summary>
    /// This module provides the WebRTC voice interface for viewer clients..
    /// 
    /// In particular, it provides the following capabilities:
    ///      ProvisionVoiceAccountRequest, VoiceSignalingRequest, and limited ChatSessionRequest.    
    /// which are the user interface to the voice service.
    /// 
    /// Initially, when the user connects to the region, the region feature "VoiceServiceType" is
    /// set to "webrtc" and the capabilities that support voice are enabled.
    /// The capabilities then pass the user request information to the IWebRtcVoiceService interface
    /// that has been registered for the reqion.
    /// </summary>
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "RegionVoiceModule")]
    public class WebRtcVoiceRegionModule : ISharedRegionModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly string logHeader = "[REGION WEBRTC VOICE]";

        private static byte[] llsdUndefAnswerBytes = Util.UTF8.GetBytes("<llsd><undef /></llsd>");

        private bool _MessageDetails = false;

        // Control info
        private static bool m_Enabled = false;

        private IConfig m_Config;

        // ISharedRegionModule.Initialize
        public void Initialise(IConfigSource config)
        {
            m_Config = config.Configs["WebRtcVoice"];
            if (m_Config is not null)
            {
                m_Enabled = m_Config.GetBoolean("Enabled", false);
                if (m_Enabled)
                {
                    _MessageDetails = m_Config.GetBoolean("MessageDetails", false);

                    m_log.Info($"{logHeader}: enabled");
                }
            }
        }

        // ISharedRegionModule.PostInitialize
        public void PostInitialise()
        {
        }

        // ISharedRegionModule.AddRegion
        public void AddRegion(Scene scene)
        {
            // TODO: register module to get parcel changes, etc
        }

        // ISharedRegionModule.RemoveRegion
        public void RemoveRegion(Scene scene)
        {
        }

        // ISharedRegionModule.RegionLoaded
        public void RegionLoaded(Scene scene)
        {
            if (m_Enabled)
            {
                // Get the hook that means Capbibilities are being registered
                scene.EventManager.OnRegisterCaps += (UUID agentID, Caps caps) =>
                    {
                        OnRegisterCaps(scene, agentID, caps);
                    };
                // Register for the region feature reporting so we can add 'webrtc'
                var sfm = scene.RequestModuleInterface<ISimulatorFeaturesModule>();
                sfm?.AddFeature("VoiceServerType", OSD.FromString("webrtc"));
            }
        }

        // ISharedRegionModule.Close
        public void Close()
        {
        }

        // ISharedRegionModule.Name
        public string Name
        {
            get { return "RegionVoiceModule"; }
        }

        // ISharedRegionModule.ReplaceableInterface
        public Type ReplaceableInterface
        {
            get { return null; }
        }

        // <summary>
        // OnRegisterCaps is invoked via the scene.EventManager
        // everytime OpenSim hands out capabilities to a client
        // (login, region crossing). We contribute three capabilities to
        // the set of capabilities handed back to the client:
        // ProvisionVoiceAccountRequest, VoiceSignalingRequest, and limited ChatSessionRequest.
        //
        // ProvisionVoiceAccountRequest allows the client to obtain
        // voice communication information the the avater.
        //
        // VoiceSignalingRequest: Used for trickling ICE candidates.
        //
        // ChatSessionRequest: Used for starting and stopping P2P voice sessions between users.
        // The viewer sends this request when the user tries to start a P2P text or voice
        // session with another user. We need to generate a new session ID and return it to the client.
        //
        // Note that OnRegisterCaps is called here via a closure
        // delegate containing the scene of the respective region (see
        // Initialise()).
        // </summary>
        public void OnRegisterCaps(Scene scene, UUID agentID, Caps caps)
        {
            m_log.DebugFormat(
                "{0}: OnRegisterCaps() called with agentID {1} caps {2} in scene {3}",
                logHeader, agentID, caps, scene.RegionInfo.RegionName);

            caps.RegisterSimpleHandler("ProvisionVoiceAccountRequest",
                    new SimpleStreamHandler("/" + UUID.Random(), (IOSHttpRequest httpRequest, IOSHttpResponse httpResponse) =>
                    {
                        ProvisionVoiceAccountRequest(httpRequest, httpResponse, agentID, scene);
                    }));

            caps.RegisterSimpleHandler("VoiceSignalingRequest",
                    new SimpleStreamHandler("/" + UUID.Random(), (IOSHttpRequest httpRequest, IOSHttpResponse httpResponse) =>
                    {
                        VoiceSignalingRequest(httpRequest, httpResponse, agentID, scene);
                    }));

            caps.RegisterSimpleHandler("ChatSessionRequest",
                    new SimpleStreamHandler("/" + UUID.Random(), (IOSHttpRequest httpRequest, IOSHttpResponse httpResponse) =>
                    {
                        ChatSessionRequest(httpRequest, httpResponse, agentID, scene);
                    }));

        }

        /// <summary>
        /// Callback for a client request for Voice Account Details
        /// </summary>
        /// <param name="scene">current scene object of the client</param>
        /// <param name="request"></param>
        /// <param name="path"></param>
        /// <param name="param"></param>
        /// <param name="agentID"></param>
        /// <param name="caps"></param>
        /// <returns></returns>
        public void ProvisionVoiceAccountRequest(IOSHttpRequest request, IOSHttpResponse response, UUID agentID, Scene scene)
        {
            // Get the voice service. If it doesn't exist, return an error.
            IWebRtcVoiceService voiceService = scene.RequestModuleInterface<IWebRtcVoiceService>();
            if (voiceService is null)
            {
                m_log.ErrorFormat("{0}[ProvisionVoice]: avatar \"{1}\": no voice service", logHeader, agentID);
                response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            if (request.HttpMethod != "POST")
            {
                m_log.DebugFormat("[{0}][ProvisionVoice]: Not a POST request. Agent={1}", logHeader, agentID.ToString());
                response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            // Deserialize the request. Convert the LLSDXml to OSD for our use
            OSDMap? map = BodyToMap(request, "[ProvisionVoiceAccountRequest]");
            if (map is null)
            {
                m_log.ErrorFormat("{0}[ProvisionVoice]: No request data found. Agent={1}", logHeader, agentID.ToString());
                response.StatusCode = (int)HttpStatusCode.NoContent;
                return;
            }

            // Make sure the request is for WebRtc voice
            if (map.TryGetValue("voice_server_type", out OSD vstosd))
            {
                if (vstosd is OSDString vst && !((string)vst).Equals("webrtc", StringComparison.OrdinalIgnoreCase))
                {
                    m_log.WarnFormat("{0}[ProvisionVoice]: voice_server_type is not 'webrtc'. Request: {1}", logHeader, map.ToString());
                    response.RawBuffer = llsdUndefAnswerBytes;
                    return;
                }
            }

            if (_MessageDetails) m_log.DebugFormat($"{logHeader}[ProvisionVoice]: request: {map}");

            if (map.TryGetString("channel_type", out string channelType))
            {
                //do fully not trust viewers voice parcel requests
                if (channelType == "local")
                {
                    if (!scene.RegionInfo.EstateSettings.AllowVoice)
                    {
                        m_log.Debug($"{logHeader}[ProvisionVoice]:region \"{scene.Name}\": voice not enabled in estate settings");
                        response.RawBuffer = llsdUndefAnswerBytes;
                        response.StatusCode = (int)HttpStatusCode.NotImplemented;
                        return;
                    }
                    if (scene.LandChannel == null)
                    {
                        m_log.Error($"{logHeader}[ProvisionVoice] region \"{scene.Name}\" land data not yet available");
                        response.RawBuffer = llsdUndefAnswerBytes;
                        response.StatusCode = (int)HttpStatusCode.NotImplemented;
                        return;
                    }

                    if (!scene.TryGetScenePresence(agentID, out ScenePresence sp))
                    {
                        m_log.Debug($"{logHeader}[ProvisionVoice]:avatar not found");
                        response.RawBuffer = llsdUndefAnswerBytes;
                        response.StatusCode = (int)HttpStatusCode.NotFound;
                        return;
                    }

                    if (map.TryGetInt("parcel_local_id", out int parcelID))
                    {
                        ILandObject parcel = scene.LandChannel.GetLandObject(parcelID);
                        if (parcel == null)
                        {
                            response.RawBuffer = llsdUndefAnswerBytes;
                            response.StatusCode = (int)HttpStatusCode.NotFound;
                            return;
                        }

                        LandData land = parcel.LandData;
                        if (land == null)
                        {
                            response.RawBuffer = llsdUndefAnswerBytes;
                            response.StatusCode = (int)HttpStatusCode.NotFound;
                            return;
                        }

                        if (!scene.RegionInfo.EstateSettings.TaxFree && (land.Flags & (uint)ParcelFlags.AllowVoiceChat) == 0)
                        {
                            m_log.Debug($"{logHeader}[ProvisionVoice]:parcel voice not allowed");
                            response.RawBuffer = llsdUndefAnswerBytes;
                            response.StatusCode = (int)HttpStatusCode.Forbidden;
                            return;
                        }

                        if ((land.Flags & (uint)ParcelFlags.UseEstateVoiceChan) != 0)
                        {
                            // By removing the parcel_local_id, the voice service will treat this as an estate channel
                            //    request and return the appropriate voice credentials for the estate channel
                            //    instead of a parcel channel
                            map.Remove("parcel_local_id"); // estate channel
                        }
                        else if (parcel.IsRestrictedFromLand(agentID) || parcel.IsBannedFromLand(agentID))
                        {
                            // check Z distance?
                            m_log.Debug($"{logHeader}[ProvisionVoice]:agent not allowed on parcel");
                            response.RawBuffer = llsdUndefAnswerBytes;
                            response.StatusCode = (int)HttpStatusCode.Forbidden;
                            return;
                        }
                    }
                }
            }

            // The checks passed. Send the request to the voice service.
            OSDMap resp = voiceService.ProvisionVoiceAccountRequest(map, agentID, scene.RegionInfo.RegionID).Result;

            if (_MessageDetails) m_log.DebugFormat("{0}[ProvisionVoice]: response: {1}", logHeader, resp.ToString());

            // TODO: check for errors and package the response

            // Convert the OSD to LLSDXml for the response
            string xmlResp = OSDParser.SerializeLLSDXmlString(resp);

            response.StatusCode = (int)HttpStatusCode.OK;
            response.RawBuffer = Util.UTF8.GetBytes(xmlResp);
            return;
        }

        public void VoiceSignalingRequest(IOSHttpRequest request, IOSHttpResponse response, UUID agentID, Scene scene)
        {
            IWebRtcVoiceService voiceService = scene.RequestModuleInterface<IWebRtcVoiceService>();
            if (voiceService is null)
            {
                m_log.ErrorFormat("{0}[VoiceSignalingRequest]: avatar \"{1}\": no voice service", logHeader, agentID);
                response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            if (request.HttpMethod != "POST")
            {
                m_log.ErrorFormat("[{0}][VoiceSignaling]: Not a POST request. Agent={1}", logHeader, agentID.ToString());
                response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            // Deserialize the request. Convert the LLSDXml to OSD for our use
            OSDMap? map = BodyToMap(request, "VoiceSignalingRequest");
            if (map is null)
            {
                m_log.ErrorFormat("{0}[VoiceSignalingRequest]: No request data found. Agent={1}", logHeader, agentID.ToString());
                response.StatusCode = (int)HttpStatusCode.NoContent;
                return;
            }

            // Make sure the request is for WebRTC voice
            if (map.TryGetValue("voice_server_type", out OSD vstosd))
            {
                if (vstosd is OSDString vst && !((string)vst).Equals("webrtc", StringComparison.OrdinalIgnoreCase))
                {
                    response.RawBuffer = llsdUndefAnswerBytes;
                    return;
                }
            }

            OSDMap resp = voiceService.VoiceSignalingRequest(map, agentID, scene.RegionInfo.RegionID).Result;
            if (_MessageDetails) m_log.DebugFormat("{0}[VoiceSignalingRequest]: Response: {1}", logHeader, resp);

            // TODO: check for errors and package the response

            response.StatusCode = (int)HttpStatusCode.OK;
            response.RawBuffer = llsdUndefAnswerBytes;
            return;
        }

        /// <summary>
        /// Callback for a client request for ChatSessionRequest.
        /// The viewer sends this request when the user tries to start a P2P text or voice session
        /// with another user. We need to generate a new session ID and return it to the client.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="response"></param>
        /// <param name="agentID"></param>
        /// <param name="scene"></param>
        public void ChatSessionRequest(IOSHttpRequest request, IOSHttpResponse response, UUID agentID, Scene scene)
        {
            m_log.DebugFormat("{0}: ChatSessionRequest received for agent {1} in scene {2}", logHeader, agentID, scene.RegionInfo.RegionName);
            if (request.HttpMethod != "POST")
            {
                response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            if (!scene.TryGetScenePresence(agentID, out ScenePresence sp) || sp.IsDeleted)
            {
                m_log.Warn($"{logHeader} ChatSessionRequest: scene presence not found or deleted for agent {agentID}");
                response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            OSDMap? reqmap = BodyToMap(request, "[ChatSessionRequest]");
            if (reqmap is null)
            {
                m_log.Warn($"{logHeader} ChatSessionRequest: message body not parsable in request for agent {agentID}");
                response.StatusCode = (int)HttpStatusCode.NoContent;
                return;
            }

            m_log.Debug($"{logHeader} ChatSessionRequest");

            if (!reqmap.TryGetString("method", out string method))
            {
                m_log.Warn($"{logHeader} ChatSessionRequest: missing required 'method' field in request for agent {agentID}");
                response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            if (!reqmap.TryGetUUID("session-id", out UUID sessionID))
            {
                m_log.Warn($"{logHeader} ChatSessionRequest: missing required 'session-id' field in request for agent {agentID}");
                response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            switch (method.ToLower())
            {
                // Methods we do not implement but which do not gate session initialisation.
                // Answering OK is enough; the viewer does not wait on a reply for these.
                case "decline p2p voice":
                case "decline invitation":
                case "accept invitation":
                case "call":
                case "invite":
                case "mute update":
                case "session update":
                    response.StatusCode = (int)HttpStatusCode.OK;
                    break;

                // chatterBoxHistoryCoro (llimview.cpp) only does anything if the response body
                // is an ARRAY, and merely logs otherwise. Answer an explicit empty array: this
                // is fired automatically after every successful "start p2p voice" when the
                // viewer's FetchGroupChatHistory setting is on, so it is a hot path.
                case "fetch history":
                    response.StatusCode = (int)HttpStatusCode.OK;
                    response.RawBuffer = Util.UTF8.GetBytes("<llsd><array /></llsd>");
                    break;

                // Ad-hoc/conference chat is not supported here. Returning a bare OK leaves the
                // viewer waiting out its full 30s SESSION_INITIALIZATION_TIMEOUT before it
                // reports anything, so reply with an explicit failure instead and let it fail
                // fast. NOTE the error field is a strings.xml KEY, resolved through
                // LLTrans::getString (llimview.cpp LLIMMgr::showSessionStartError) — an
                // arbitrary string renders as a missing-string placeholder, so use a real one.
                case "start conference":
                    IEventQueue confQueue = scene.RequestModuleInterface<IEventQueue>();
                    if (confQueue is null)
                    {
                        response.StatusCode = (int)HttpStatusCode.InternalServerError;
                        break;
                    }
                    confQueue.ChatterBoxSessionStartReply(
                            sessionID, string.Empty, 1,
                            false, true, sessionID,
                            false, "no_ability_error", agentID);
                    response.StatusCode = (int)HttpStatusCode.OK;
                    break;
                // Asking to start a P2P voice session. We need to generate a new session ID and return
                //     it to the client in a ChatterBoxSessionStartReply event.
                case "start p2p voice":
                    // The viewer's P2P session id is the XOR of the two agent ids
                    // (llimview.cpp LLIMMgr::computeSessionID), and that same id is what
                    // inbound UDP ImprovedInstantMessage traffic is filed under. So it must be
                    // reproduced exactly. A random id here would be worse than an error: the
                    // viewer re-keys its floater to whatever we return
                    // (processSessionInitializedReply -> setKey), after which arriving IMs land
                    // in a different session than the one the user is typing into. If "params"
                    // is absent we cannot compute it, so refuse rather than invent one.
                    if (!reqmap.TryGetUUID("params", out UUID otherID))
                    {
                        m_log.Warn($"{logHeader} ChatSessionRequest: 'start p2p voice' with no 'params' for agent {agentID}");
                        response.StatusCode = (int)HttpStatusCode.BadRequest;
                        break;
                    }
                    UUID newSessionID = new(otherID.ulonga ^ agentID.ulonga, otherID.ulongb ^ agentID.ulongb);

                    IEventQueue queue = scene.RequestModuleInterface<IEventQueue>();
                    if (queue is null)
                    {
                        m_log.ErrorFormat("{0}: no event queue for scene {1}", logHeader, scene.RegionInfo.RegionName);
                        response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    }
                    else
                    {
                        queue.ChatterBoxSessionStartReply(
                                newSessionID,
                                sp.Name,
                                2,
                                false,
                                true,
                                sessionID,
                                true,
                                string.Empty,
                                agentID);

                        response.StatusCode = (int)HttpStatusCode.OK;
                    }
                    break;
                default:
                    response.StatusCode = (int)HttpStatusCode.BadRequest;
                    break;
            }
        }

        /// <summary>
        /// Convert the LLSDXml body of the request to an OSDMap for easier handling.
        /// Also logs the request if message details is enabled.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="pCaller"></param>
        /// <returns>'null' if the request body is empty or cannot be deserialized</returns>
        private OSDMap? BodyToMap(IOSHttpRequest request, string pCaller)
        {
            OSDMap? map = null;
            try
            {
                using (Stream inputStream = request.InputStream)
                {
                    if (inputStream.Length > 0)
                    {
                        OSD tmp = OSDParser.DeserializeLLSDXml(inputStream);
                        if (_MessageDetails) m_log.DebugFormat("{0} BodyToMap: Request: {1}", pCaller, tmp.ToString());
                        map = tmp as OSDMap;
                    }
                }
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("{0} BodyToMap: Exception: {1}", pCaller, ex);
                map = null;
            }
            return map;
        }


    }
}
