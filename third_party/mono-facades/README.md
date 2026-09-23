# Mono compatibility facades

These 30 architecture-neutral managed facade assemblies are copied from
Ubuntu 24.04's Mono 6.8.0.105+dfsg-3.6ubuntu2 package
(/usr/lib/mono/4.5/Facades).

They are the minimal facade set obtained by intersecting the direct
System.* assembly references of GLoader's Roslyn 2.10 runtime payload
with Mono's runtime facade directory. The set was then validated by
compiling and loading a raw C# source mod inside the exact Steam Linux
TerrariaServer 1.4.5.8 MonoKickstart runtime.

GLoader does not use or ship a second Mono runtime. These tiny facade
assemblies only bridge Roslyn's contract assembly references to the
framework implementation already present in Terraria's embedded Mono
environment.

Mono runtime/class-library code is generally MIT-licensed; retain the
project's THIRD-PARTY-NOTICES.txt when redistributing GLoader.
