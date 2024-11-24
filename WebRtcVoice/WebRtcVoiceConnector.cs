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

using OpenMetaverse;
using OpenMetaverse.StructuredData;

using log4net;
using Nini.Config;
using System.Threading.Tasks;

namespace WebRtcVoice
{
    public class WebRtcVoiceConnector : IWebRtcVoiceService
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public WebRtcVoiceConnector(IConfigSource config)
        {
            // Nothing to do
        }

        public IVoiceViewerSession CreateViewerSession(OSDMap pRequest, UUID pUserID, IScene pScene)
        {
            throw new NotImplementedException();
        }

        public Task<OSDMap> ProvisionVoiceAccountRequest(OSDMap pRequest, UUID pUserID, IScene pScene)
        {
            throw new NotImplementedException();
        }

        public Task<OSDMap> ProvisionVoiceAccountRequest(IVoiceViewerSession pVSession, OSDMap pRequest, UUID pUserID, IScene pScene)
        {
            throw new NotImplementedException();
        }

        public Task<OSDMap> VoiceSignalingRequest(OSDMap pRequest, UUID pUserID, IScene pScene)
        {
            throw new NotImplementedException();
        }

        public Task<OSDMap> VoiceSignalingRequest(IVoiceViewerSession pVSession, OSDMap pRequest, UUID pUserID, IScene pScene)
        {
            throw new NotImplementedException();
        }
    }
}

