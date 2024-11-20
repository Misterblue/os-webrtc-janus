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

using OpenMetaverse.StructuredData;

namespace WebRtcVoice
{
    public class JanusViewerSession : IVoiceViewerSession
    {
        // 'viewer_session' that is passed to and from the viewer
        public string SessionID { get; set; }
        public JanusRoom Room { get; set; }
        public string OfferOrig { get; set; }
        public string Offer { get; set; }
        // Contains "type" and "sdp" fields
        public OSDMap Answer { get; set; }

        // The simulator has a GUID to identify the user
        public string AgentId { get; set; }
        // The Janus server keeps track of the user by this ID
        public int JanusAttendeeId;

        public JanusViewerSession(string pSessionID)
        {
            SessionID = pSessionID;
        }
    }
}
