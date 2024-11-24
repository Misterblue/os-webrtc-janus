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

using OpenSim.Server.Base;
using OpenSim.Services.Base;
using OpenSim.Services.Interfaces;

using OpenMetaverse.StructuredData;
using OpenMetaverse;
using OpenSim.Framework;

using Nini.Config;
using log4net;
using System.Threading.Tasks;

namespace WebRtcVoice
{
    public interface IVoiceViewerSession
    {
        public string ViewerSessionID { get; set; }
        public IWebRtcVoiceService VoiceService { get; set; }
    }
}
