"""Explicit simulated DOS layouts; neither changes the installed game."""

LAYOUTS = {
    "legacy": dict(profile="legacy", arena_start=0x1000,
                   environment_segment=0x0800, conventional_top=0xa000),
    # Pinned Darklands+ port/runtime/main.c LOADSEG0200, PSP01f0, env0101.
    "compact": dict(profile="compact", arena_start=0x01f0,
                    environment_segment=0x0101, conventional_top=0xa000),
}


def resolve_layout(profile):
    if profile not in LAYOUTS:
        raise ValueError("memory layout must be legacy or compact")
    return dict(LAYOUTS[profile])


def recorded_layout(report):
    """Historical captures predate the field and used the legacy profile."""
    value = report.get("memory_layout")
    if value is None:
        return resolve_layout("legacy")
    if not isinstance(value, dict):
        raise ValueError("invalid recorded memory layout")
    expected = resolve_layout(value.get("profile"))
    if value != expected:
        raise ValueError("recorded memory layout disagrees with its profile")
    return expected
