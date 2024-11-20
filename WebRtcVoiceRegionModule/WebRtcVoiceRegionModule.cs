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
    ///      ProvisionVoiceAccountRequest, VoiceSignalingRequest, and ParcelVoiceInfoRequest.    
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

        // Control info
        private static bool m_Enabled = false;

        private readonly Dictionary<string, string> m_UUIDName = new Dictionary<string, string>();
        private Dictionary<string, string> m_ParcelAddress = new Dictionary<string, string>();

        private IConfig m_Config;

        // ISharedRegionModule.Initialize
        public void Initialise(IConfigSource config)
        {
            m_log.Info($"{logHeader}: RegionVoiceModule initializing");
            m_Config = config.Configs["WebRtcVoice"];
            if (m_Config is null) return;

            m_Enabled = m_Config.GetBoolean("Enabled", false);
            if (!m_Enabled) return;

            try
            {
                m_log.Info($"{logHeader}: plugin enabled");
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("{0}: plugin initialization failed: {1} {2}", logHeader, e.Message, e.StackTrace);
                return;
            }
        }

        // ISharedRegionModule.PostInitialize
        public void PostInitialise()
        {
        }

        // ISharedRegionModule.AddRegion
        public void AddRegion(Scene scene)
        {
            if (m_Enabled)
            {
                // Get the hook that means Capbibilities are being registered
                scene.EventManager.OnRegisterCaps += (UUID agentID, Caps caps) =>
                    {
                        OnRegisterCaps(scene, agentID, caps);
                    };
            }
        }

        // ISharedRegionModule.RemoveRegion
        public void RemoveRegion(Scene scene)
        {
            var sfm = scene.RequestModuleInterface<ISimulatorFeaturesModule>();
            sfm.OnSimulatorFeaturesRequest -= OnSimulatorFeatureRequestHandler;
        }

        // ISharedRegionModule.RegionLoaded
        public void RegionLoaded(Scene scene)
        {
            if (m_Enabled)
            {
                // Register for the region feature reporting so we can add 'webrtc'
                var sfm = scene.RequestModuleInterface<ISimulatorFeaturesModule>();
                sfm.OnSimulatorFeaturesRequest += OnSimulatorFeatureRequestHandler;
                m_log.DebugFormat("{0}: registering OnSimulatorFeatureRequestHandler", logHeader);
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

        // Called when the simulator features are being constructed.
        // Add the flag that says we support WebRtc voice.
        private void OnSimulatorFeatureRequestHandler(UUID agentID, ref OSDMap features)
        {
            m_log.DebugFormat("{0}: setting VoiceServerType=webrtc for agent {1}", logHeader, agentID);
            features["VoiceServerType"] = "webrtc";
        }

        // <summary>
        // OnRegisterCaps is invoked via the scene.EventManager
        // everytime OpenSim hands out capabilities to a client
        // (login, region crossing). We contribute three capabilities to
        // the set of capabilities handed back to the client:
        // ProvisionVoiceAccountRequest, VoiceSignalingRequest, and ParcelVoiceInfoRequest.
        //
        // ProvisionVoiceAccountRequest allows the client to obtain
        // voice communication information the the avater.
        //
        // VoiceSignalingRequest: Used for trickling ICE candidates.
        //
        // ParcelVoiceInfoRequest is invoked whenever the client
        // changes from one region or parcel to another.
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

            caps.RegisterSimpleHandler("ParcelVoiceInfoRequest",
                    new SimpleStreamHandler("/" + UUID.Random(), (IOSHttpRequest httpRequest, IOSHttpResponse httpResponse) =>
                    {
                        ParcelVoiceInfoRequest(httpRequest, httpResponse, agentID, scene);
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
            if(request.HttpMethod != "POST")
            {
                m_log.DebugFormat("[{0}][ProvisionVoice]: Not a POST request. Agent={1}", logHeader, agentID.ToString());
                response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            m_log.DebugFormat("{0}[ProvisionVoice]: Request for {1}", logHeader, agentID.ToString());

            // Deserialize the request
            OSDMap map = null;
            using (Stream inputStream = request.InputStream)
            {
                if (inputStream.Length > 0)
                {
                    OSD tmp = OSDParser.DeserializeLLSDXml(inputStream);
                    m_log.DebugFormat("{0}[ProvisionVoice]: Request: {1}", logHeader, tmp.ToString());

                    if (tmp is OSDMap)
                    {
                        map = (OSDMap)tmp;
                    }
                }
            }
            if (map is null)
            {
                m_log.DebugFormat("{0}[ProvisionVoice]: No request data found. Agent={1}", logHeader, agentID.ToString());
                response.StatusCode = (int)HttpStatusCode.NoContent;
                return;
            }

            IWebRtcVoiceService voiceService = scene.RequestModuleInterface<IWebRtcVoiceService>();
            if (voiceService is null)
            {
                m_log.ErrorFormat("{0}[ProvisionVoice]: avatar \"{1}\": no voice service", logHeader, agentID);
                response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            // Make sure the request is for WebRtc voice
            if (map.TryGetValue("voice_server_type", out OSD vstosd))
            {
                if (vstosd is OSDString vst && !((string)vst).Equals("webrtc", StringComparison.OrdinalIgnoreCase))
                {
                    m_log.WarnFormat("{0}[ProvisionVoice]: voice_server_type is not 'webrtc'. Request: {1}", logHeader, map.ToString());
                    response.RawBuffer = Util.UTF8.GetBytes("<llsd><undef /></llsd>");
                    return;
                }
            }

            // See if a 'logout' request is present
            if (map.TryGetValue("logout", out OSD logout))
            {
                if (logout is OSDBoolean lob && lob)
                {
                    m_log.DebugFormat("[{0}][ProvisionVoice]: avatar \"{1}\": logout", logHeader, agentID);
                    // The logout request is handled by the voice service (to tear down the connection)
                    OSDMap logoutResp = voiceService.ProvisionVoiceAccountRequest(map, agentID, scene).Result;
                }
            }

            OSDMap resp = voiceService.ProvisionVoiceAccountRequest(map, agentID, scene).Result;

            m_log.DebugFormat("{0}[ProvisionVoice]: response: {1}", logHeader, resp.ToString());

            // TODO: check for erros and package the response
            string xmlResp = OSDParser.SerializeLLSDXmlString(resp);

            response.StatusCode = (int)HttpStatusCode.OK;
            response.RawBuffer = Util.UTF8.GetBytes(xmlResp);
            return;
        }

        public void VoiceSignalingRequest(IOSHttpRequest request, IOSHttpResponse response, UUID agentID, Scene scene)
        {
            if(request.HttpMethod != "POST")
            {
                m_log.DebugFormat("[{0}][VoiceSignaling]: Not a POST request. Agent={1}", logHeader, agentID.ToString());
                response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            m_log.DebugFormat("{0}[VoiceSignaling]: Request for {1}", logHeader, agentID.ToString());

            // Deserialize the request
            OSDMap map = null;
            using (Stream inputStream = request.InputStream)
            {
                if (inputStream.Length > 0)
                {
                    OSD tmp = OSDParser.DeserializeLLSDXml(inputStream);
                    m_log.DebugFormat("{0}[VoiceSignaling]: Request: {1}", logHeader, tmp.ToString());

                    if (tmp is OSDMap)
                    {
                        map = (OSDMap)tmp;
                    }
                }
            }
            if (map is null)
            {
                m_log.DebugFormat("{0}[VoiceSignaling]: No request data found. Agent={1}", logHeader, agentID.ToString());
                response.StatusCode = (int)HttpStatusCode.NoContent;
                return;
            }

            // Make sure the request is for WebRTC voice
            if (map.TryGetValue("voice_server_type", out OSD vstosd))
            {
                if (vstosd is OSDString vst && !((string)vst).Equals("webrtc", StringComparison.OrdinalIgnoreCase))
                {
                    response.RawBuffer = Util.UTF8.GetBytes("<llsd><undef /></llsd>");
                    return;
                }
            }

            IWebRtcVoiceService voiceService = scene.RequestModuleInterface<IWebRtcVoiceService>();
            if (voiceService is null)
            {
                m_log.ErrorFormat("{0}[VoiceSignalingRequest]: avatar \"{1}\": no voice service", logHeader, agentID);
                response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            m_log.DebugFormat("{0}[VoiceSignalingRequest]: message: {1}", logHeader, map.ToString());
            OSDMap resp = voiceService.VoiceSignalingRequest(map, agentID, scene).Result;

            // TODO: check for erros and package the response
            m_log.DebugFormat("{0}[VoiceSignalingRequest]: message: {1}", logHeader, map.ToString());

            response.StatusCode = (int)HttpStatusCode.OK;
            response.RawBuffer = Util.UTF8.GetBytes("<llsd><undef /></llsd>");
            return;
        }

        /// <summary>
        /// Callback for a client request for ParcelVoiceInfo
        /// </summary>
        /// <param name="scene">current scene object of the client</param>
        /// <param name="request"></param>
        /// <param name="path"></param>
        /// <param name="param"></param>
        /// <param name="agentID"></param>
        /// <param name="caps"></param>
        /// <returns></returns>
        public void ParcelVoiceInfoRequest(IOSHttpRequest request, IOSHttpResponse response, UUID agentID, Scene scene)
        {
            if (request.HttpMethod != "POST")
            {
                response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            response.StatusCode = (int)HttpStatusCode.OK;

            m_log.DebugFormat(
                "{0}[PARCELVOICE]: ParcelVoiceInfoRequest() on {1} for {2}",
                logHeader, scene.RegionInfo.RegionName, agentID);

            ScenePresence avatar = scene.GetScenePresence(agentID);
            if(avatar == null)
            {
                response.RawBuffer = Util.UTF8.GetBytes("<llsd>undef</llsd>");
                return;
            }

            string avatarName = avatar.Name;

            // - check whether we have a region channel in our cache
            // - if not:
            //       create it and cache it
            // - send it to the client
            // - send channel_uri: as "sip:regionID@m_sipDomain"
            try
            {
                string channelUri;

                if (null == scene.LandChannel)
                {
                    m_log.ErrorFormat("region \"{0}\": avatar \"{1}\": land data not yet available",
                                                      scene.RegionInfo.RegionName, avatarName);
                    response.RawBuffer = Util.UTF8.GetBytes("<llsd>undef</llsd>");
                    return;
                }

                // get channel_uri: check first whether estate
                // settings allow voice, then whether parcel allows
                // voice, if all do retrieve or obtain the parcel
                // voice channel
                LandData land = scene.GetLandData(avatar.AbsolutePosition);

                // TODO: EstateSettings don't seem to get propagated...
                 if (!scene.RegionInfo.EstateSettings.AllowVoice)
                 {
                     m_log.DebugFormat("{0}[PARCELVOICE]: region \"{1}\": voice not enabled in estate settings",
                                       logHeader, scene.RegionInfo.RegionName);
                    channelUri = String.Empty;
                }
                else

                if (!scene.RegionInfo.EstateSettings.TaxFree && (land.Flags & (uint)ParcelFlags.AllowVoiceChat) == 0)
                {
                    channelUri = String.Empty;
                }
                else
                {
                    channelUri = ChannelUri(scene, land);
                }

                // fast foward encode
                osUTF8 lsl = LLSDxmlEncode2.Start(512);
                LLSDxmlEncode2.AddMap(lsl);
                LLSDxmlEncode2.AddElem("parcel_local_id", land.LocalID, lsl);
                LLSDxmlEncode2.AddElem("region_name", scene.Name, lsl);
                LLSDxmlEncode2.AddMap("voice_credentials", lsl);
                LLSDxmlEncode2.AddElem("channel_uri", channelUri, lsl);
                //LLSDxmlEncode2.AddElem("channel_credentials", channel_credentials, lsl);
                LLSDxmlEncode2.AddEndMap(lsl);
                LLSDxmlEncode2.AddEndMap(lsl);

                response.RawBuffer= LLSDxmlEncode2.EndToBytes(lsl);
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("{0}[PARCELVOICE]: region \"{1}\": avatar \"{2}\": {3}, retry later",
                                  logHeader, scene.RegionInfo.RegionName, avatarName, e.Message);
                m_log.DebugFormat("{0}[PARCELVOICE]: region \"{1}\": avatar \"{2}\": {3} failed",
                                  logHeader, scene.RegionInfo.RegionName, avatarName, e.ToString());

                response.RawBuffer = Util.UTF8.GetBytes("<llsd>undef</llsd>");
            }
        }

        // Not sure what this Uri is for. Is this FreeSwitch specific?
        // TODO: is this useful for WebRtc?
        private string ChannelUri(Scene scene, LandData land)
        {
            string channelUri = null;

            string landUUID;
            string landName;

            // Create parcel voice channel. If no parcel exists, then the voice channel ID is the same
            // as the directory ID. Otherwise, it reflects the parcel's ID.

            lock (m_ParcelAddress)
            {
                if (m_ParcelAddress.ContainsKey(land.GlobalID.ToString()))
                {
                    m_log.DebugFormat("{0}: parcel id {1}: using sip address {2}",
                                      logHeader, land.GlobalID, m_ParcelAddress[land.GlobalID.ToString()]);
                    return m_ParcelAddress[land.GlobalID.ToString()];
                }
            }

            if (land.LocalID != 1 && (land.Flags & (uint)ParcelFlags.UseEstateVoiceChan) == 0)
            {
                landName = String.Format("{0}:{1}", scene.RegionInfo.RegionName, land.Name);
                landUUID = land.GlobalID.ToString();
                m_log.DebugFormat("{0}: Region:Parcel \"{1}\": parcel id {2}: using channel name {3}",
                                  logHeader, landName, land.LocalID, landUUID);
            }
            else
            {
                landName = String.Format("{0}:{1}", scene.RegionInfo.RegionName, scene.RegionInfo.RegionName);
                landUUID = scene.RegionInfo.RegionID.ToString();
                m_log.DebugFormat("{0}: Region:Parcel \"{1}\": parcel id {2}: using channel name {3}",
                                  logHeader, landName, land.LocalID, landUUID);
            }

            // slvoice handles the sip address differently if it begins with confctl, hiding it from the user in
            // the friends list. however it also disables the personal speech indicators as well unless some
            // siren14-3d codec magic happens. we dont have siren143d so we'll settle for the personal speech indicator.
            channelUri = String.Format("sip:conf-{0}@{1}",
                     "x" + Convert.ToBase64String(Encoding.ASCII.GetBytes(landUUID)),
                     /*m_freeSwitchRealm*/ "webRTC");

            lock (m_ParcelAddress)
            {
                if (!m_ParcelAddress.ContainsKey(land.GlobalID.ToString()))
                {
                    m_ParcelAddress.Add(land.GlobalID.ToString(),channelUri);
                }
            }

            return channelUri;
        }

    }
}
