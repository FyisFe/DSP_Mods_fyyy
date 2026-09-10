"""Run Checks.exe in DSP's bundled Mono, in a separate process from the game.

Build Checks.csproj first. Usage: python run_mono.py <game-managed-dir> <bepinex-core-dir>
Embedding API: https://www.mono-project.com/docs/advanced/embedding/
"""

import ctypes as c
from pathlib import Path
import sys


def main():
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    managed, core = (Path(arg).resolve(strict=True) for arg in sys.argv[1:])
    runtime = managed.parent.parent / "MonoBleedingEdge"
    executable = Path(__file__).parent / "bin/Release/net472/Checks.exe"
    executable = executable.resolve(strict=True)
    mono = c.CDLL(str(runtime / "EmbedRuntime/mono-2.0-bdwgc.dll"))
    mono.mono_set_assemblies_path.argtypes = [c.c_char_p]
    mono.mono_set_dirs.argtypes = [c.c_char_p, c.c_char_p]
    mono.mono_config_parse.argtypes = [c.c_char_p]
    mono.mono_jit_init_version.argtypes = [c.c_char_p, c.c_char_p]
    mono.mono_jit_init_version.restype = c.c_void_p
    mono.mono_domain_assembly_open.argtypes = [c.c_void_p, c.c_char_p]
    mono.mono_domain_assembly_open.restype = c.c_void_p
    mono.mono_jit_exec.argtypes = [c.c_void_p, c.c_void_p, c.c_int, c.POINTER(c.c_char_p)]
    mono.mono_jit_cleanup.argtypes = [c.c_void_p]
    mono.mono_set_assemblies_path(str(managed).encode())
    mono.mono_set_dirs(str(managed).encode(), str(runtime / "etc").encode())
    mono.mono_config_parse(None)
    domain = mono.mono_jit_init_version(b"LogisticsProfiler.Checks", b"v4.0.30319")
    if not domain:
        raise RuntimeError("Mono initialization failed")
    try:
        assembly = mono.mono_domain_assembly_open(domain, str(executable).encode())
        if not assembly:
            raise RuntimeError("Mono could not load Checks.exe")
        # Flight-update bindings require Unity native calls, unavailable in this headless host.
        args = [str(path).encode() for path in (executable, managed, core)] + [b"--dispatch-only"]
        argv = (c.c_char_p * len(args))(*args)
        return mono.mono_jit_exec(domain, assembly, len(args), argv)
    finally:
        mono.mono_jit_cleanup(domain)


if __name__ == "__main__":
    raise SystemExit(main())
