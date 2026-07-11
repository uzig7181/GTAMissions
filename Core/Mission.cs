using System;
using GTA.UI;

namespace GTAMissions.Core
{
    /// <summary>
    /// Base class for a scripted mission. Derive from this and override
    /// <see cref="OnStart"/> and <see cref="OnUpdate"/> to implement mission logic.
    ///
    /// A <see cref="Mission"/> is a plain object (not a GTA.Script) so it can be
    /// created, ticked, and torn down under the control of a single top-level
    /// Script (see <see cref="GTAMissions.MissionManager"/>).
    /// </summary>
    public abstract class Mission
    {
        public bool IsActive { get; private set; }

        /// <summary>
        /// Raised exactly once when the mission ends, regardless of outcome.
        /// </summary>
        public event EventHandler<MissionOutcome> MissionEnded;

        /// <summary>
        /// Call once to launch the mission. Spawns peds/vehicles, creates blips,
        /// and shows the initial objective via <see cref="OnStart"/>.
        /// </summary>
        public void Start()
        {
            IsActive = true;
            OnStart();
        }

        /// <summary>
        /// Call every game tick while this mission is the active one.
        /// </summary>
        public void Tick()
        {
            if (IsActive)
            {
                OnUpdate();
            }
        }

        /// <summary>
        /// Set up the mission: spawn entities, create blips, show the first objective.
        /// </summary>
        protected abstract void OnStart();

        /// <summary>
        /// Advance mission state. Called every tick while the mission is active.
        /// </summary>
        protected abstract void OnUpdate();

        protected void ShowObjective(string text) => Screen.ShowSubtitle(text);

        protected void Succeed(string message = "Mission Passed")
        {
            End(MissionOutcome.Success, "~g~" + message);
        }

        protected void Fail(string message = "Mission Failed")
        {
            End(MissionOutcome.Failed, "~r~" + message);
        }

        /// <summary>
        /// Force-ends the mission early (e.g. player quit, script unload).
        /// </summary>
        public void Cancel()
        {
            End(MissionOutcome.Cancelled, null);
        }

        private void End(MissionOutcome outcome, string message)
        {
            if (!IsActive)
            {
                return;
            }

            IsActive = false;

            if (!string.IsNullOrEmpty(message))
            {
                Screen.ShowSubtitle(message);
            }

            SoftCleanup();
            MissionEnded?.Invoke(this, outcome);
        }

        /// <summary>
        /// "Soft" cleanup - called automatically when the mission ends (success, fail,
        /// or cancel). Should stop peds from fighting and reset the player's wanted
        /// level, but leave any spawned vehicles/peds in the world so the aftermath
        /// is still visible. Override to implement mission-specific behavior.
        /// </summary>
        public virtual void SoftCleanup()
        {
        }

        /// <summary>
        /// "Hard" cleanup - never called automatically. Wired to a dedicated key in
        /// <see cref="GTAMissions.MissionManager"/> so you can fully wipe everything
        /// this mission spawned (vehicles and peds) whenever you're done looking at it.
        /// </summary>
        public virtual void HardCleanup()
        {
        }

        /// <summary>
        /// Optional hook for mission-specific debug/testing helpers, wired to a
        /// dedicated debug key in <see cref="GTAMissions.MissionManager"/>. No-op
        /// by default; override to cycle through variants you want to test live.
        /// </summary>
        public virtual void DebugCycle()
        {
        }
    }
}
