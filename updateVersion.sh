#! /bin/bash
# Script to add the number from the VERSION file to the .csproj of the sub-projects
# Run this just after running runprebuild.sh to add versions to the .csproj files.

BASE=$(pwd)

VERSION=$(cat VERSION)

for proj in Janus WebRtcVoice WebRtcVoiceRegionModule WebRtcVoiceServiceModule ; do
    cd "$BASE"
    cd "$proj"
    sed -i "/TargetFramework/a <Version>$VERSION</Version>" *.csproj
    #dotnet msbuild -t:UpdateVersion -p:Version=$VERSION
done
