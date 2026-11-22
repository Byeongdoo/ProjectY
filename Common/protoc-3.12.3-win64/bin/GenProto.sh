#!/bin/bash
# macOS/Linux용 GenProto.bat 대응 스크립트.
# protoc 버전은 런타임(Google.Protobuf 3.12.3)과 맞춘 Common/protoc-3.12.3-osx 를 쓴다.
set -e
cd "$(dirname "$0")"

ROOT="../../.."
PROTOC="../../protoc-3.12.3-osx/bin/protoc"

"$PROTOC" -I=./ --csharp_out=./ ./Protocol.proto

dotnet build "$ROOT/Server/PacketGenerator/PacketGenerator.csproj" -c Debug -v q -nologo
dotnet "$ROOT/Server/PacketGenerator/bin/PacketGenerator.dll" ./Protocol.proto

cp -f Protocol.cs "$ROOT/Client/Assets/Scripts/Packet/"
cp -f Protocol.cs "$ROOT/Server/Server/Packet/"
cp -f ClientPacketManager.cs "$ROOT/Client/Assets/Scripts/Packet/"
cp -f ServerPacketManager.cs "$ROOT/Server/Server/Packet/"
