﻿using System.Runtime.CompilerServices;

// So I wanted to mess with NetIncomingMessage and NetOutgoingMessage from tests.
// Now.. the instructors are internal...
// Unless...
// I mean we have this project here from the weird way we're compiling Lidgren.
// I could just put this in here... it wouldn't touch the main Lidgren repo at all...
[assembly: InternalsVisibleTo("Robust.UnitTesting")]
