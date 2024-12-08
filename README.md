# os-webrtc-janus

Addon-module for OpenSimulator to provide webrtc voice using Janus-gateway.

```
# Get the OpenSimulator sources
git clone git://opensimulator.org/git/opensim
cd opensim

# Fetch the WebRtc addon
cd addon-modules
git clone https://github.com/Misterblue/os-webrtc-janus.git
cd ..

# Build the project files
./runprebuild.sh

# Compile OpenSimulator with the webrtc addon
./compile.sh

# Copy the INI file for webrtc into a config dir that is read at boot
mkdir bin/config
cp addon-modules/os-webrtc-janus/os-webrtc-janus.ini bin/config
```

