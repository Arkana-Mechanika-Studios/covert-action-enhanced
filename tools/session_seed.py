"""Read explicit save-slot/Hall-of-Fame seeds without modifying their source."""
import hashlib
from pathlib import Path
import re

from trace_startup import ROOT


def is_session_name(name):
    return bool(re.fullmatch(r"CV[0-3]\.SVE",name) or name=="FAME.DTA")


def read_seed_session(source):
    if source is None: return {},[]
    source=Path(source).resolve()
    if ROOT/"build" not in source.parents:
        raise ValueError("seed session must be an existing file or directory below this project's build directory")
    paths=sorted(source.iterdir()) if source.is_dir() else [source]
    files={}; provenance=[]
    for path in paths:
        resolved=path.resolve(); name=path.name.upper()
        if ROOT/"build" not in resolved.parents or not path.is_file() or not is_session_name(name):
            raise ValueError("seed session accepts only CV0.SVE through CV3.SVE and FAME.DTA files below build")
        if name in files: raise ValueError("duplicate seed slot "+name)
        data=path.read_bytes(); files[name]=data
        provenance.append(dict(name=name,size=len(data),sha256=hashlib.sha256(data).hexdigest(),source=str(resolved)))
    if not files: raise ValueError("empty seed session")
    return files,provenance
