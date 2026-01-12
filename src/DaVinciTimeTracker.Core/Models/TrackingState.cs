namespace DaVinciTimeTracker.Core.Models;

public enum TrackingState
{
    NotTracking,  // No session active, no time tracking
    GraceStart,   // Session < 3 min, time accumulates, no grace protection
    Tracking,     // Normal tracking, time accumulates, grace protected
    GraceEnd      // Grace period (10 min), time CONTINUES to accumulate
}

// Note: All non-NotTracking states = time accumulates (equivalent to old IsActive=true)
