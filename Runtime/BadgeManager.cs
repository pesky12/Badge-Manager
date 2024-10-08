using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using LoliPoliceDepartment.Utilities.AccountManager;
using UdonSharp;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
#if UNITY_EDITOR
using UnityEditor.Events;
using UnityEngine.Events;
using VRC.Udon;
#endif

// ReSharper disable UseIndexFromEndExpression

namespace PeskyBox.BadgeManager
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    [SuppressMessage("ReSharper", "UseArrayEmptyMethod")]
    public class BadgeManager : UdonSharpBehaviour
    {
        // Account Manager
        public OfficerAccountManager accountManager;

        // Badge Setup
        [SerializeField] private GameObject badgeCanvasPrefab;
        [SerializeField] private Vector3 badgeOffset;
        [SerializeField] private bool logEnabled = true;

        // Badge Container
        [SerializeField] private GameObject badgeContainer;

        // Badge Roles
        // This needs to be ordered from the highest rank to lowest
        [SerializeField] private string[] badgeRoles = new string[0];
        [SerializeField] private GameObject[] badgePrefabs = new GameObject[0];

        // Additive Badge Roles
        [SerializeField] private string[] additiveBadgeRoles = new string[0];
        [SerializeField] private GameObject[] additiveBadgeObjects = new GameObject[0];

        // Staff Info Badge Roles
        [SerializeField] private string[] toggleableBadgeRoles = new string[0];
        [SerializeField] private GameObject[] toggleableBadgeObjects = new GameObject[0];
        [SerializeField] private GameObject[] toggleableBadges = new GameObject[0];
        private bool areToggleableBadgesToggled;

        // Badge Owners
        private VRCPlayerApi[] _badgeOwners = new VRCPlayerApi[0];
        private GameObject[] _badgeObjects = new GameObject[0];

        // Timing
        private int _lastRefreshFrame = -1;

        // VRC Player data
        private VRCPlayerApi[] _players = new VRCPlayerApi[0];
        private VRCPlayerApi _localPlayer;

        // Player toggle request data
        [UdonSynced] private int[] _syncedHiddenPlayersID = new int[0];
        private int[] _locallyHiddenPlayersID = new int[0];
        private bool[] _badgeIsHiddenOwnersIndex = new bool[0];

        // Anti-spam
        private float localTimeStorage;
        private int localCounterStorage;
        

        // Toggles
        public Toggle[] showMyBadgeToggles;
        public Toggle[] showToggleableBadgesToggles;
        public Toggle[] showAllBadgesToggles;

        private void Start()
        {
            // Initialize variables
            _localPlayer = Networking.LocalPlayer;
            
            UpdatePlayerList();

            // Account manager go!
            accountManager.NotifyWhenInitialized(this, nameof(OnInitialized));
        }

        public void OnInitialized()
        {
            // Simulate player join for all players to populate the badge list
            foreach (var player in _players) OnPlayerJoined(player);

            // Set toggles to the correct state
            foreach (var toggle in showMyBadgeToggles) toggle.SetIsOnWithoutNotify(true);
            foreach (var toggle in showToggleableBadgesToggles) toggle.SetIsOnWithoutNotify(areToggleableBadgesToggled);

            // Sync up the badge hiding by simulating a Serialization request
            RequestSerialization();
        }

        private void LateUpdate()
        {
            // Cache clients head position
            var headPosition = _localPlayer.GetBonePosition(HumanBodyBones.Head);

            // Update badge locations for all players
            for (var i = 0; i < _badgeOwners.Length; i++)
            {
                // Don't update hidden badges
                // if (_badgeIsHiddenOwnersIndex[i]) continue;

                _badgeObjects[i].transform.position =
                    _badgeOwners[i].GetBonePosition(HumanBodyBones.Head) + badgeOffset;

                // Make the badge face the player
                var lookAt = new Vector3(headPosition.x, headPosition.y, headPosition.z);
                _badgeObjects[i].transform.LookAt(lookAt);
            }
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            // Preflight
            if (!accountManager.isReady) return;
            if (HasBadge(player)) return;

            LOG($"Player {player.displayName} joined");
            LOG(accountManager._GetBool(player.displayName, "Triskel Developer").ToString());

            if (!accountManager._IsOfficer(player.displayName)) return;

            LOG($"Player {player.displayName} joined and is in database, adding badge");

            // Create a canvas to add the badge to
            var badgeCanvas = Instantiate(badgeCanvasPrefab, player.GetBonePosition(HumanBodyBones.Head),
                Quaternion.identity);
            
            // Put the badge canvas inside a badge container
            badgeCanvas.transform.SetParent(badgeContainer.transform);
            
            // Check if the player has a role badge
            for (var i = 0; i < badgeRoles.Length; i++)
            {
                var role = badgeRoles[i];
                if (accountManager._GetBool(player.displayName, role))
                {
                    // Instantiate the badge
                    var badge = Instantiate(badgePrefabs[i], badgeCanvas.transform);

                    LOG($"Player {player.displayName} has role {role}, adding badge");

                    // We are done break the loop to prevent adding lower roles
                    break;
                }
            }


            // Check if the player has an additive badge
            for (var i = 0; i < additiveBadgeRoles.Length; i++)
            {
                var role = additiveBadgeRoles[i];
                if (accountManager._GetBool(player.displayName, role))
                {
                    // Instantiate the badge
                    var badge = Instantiate(additiveBadgeObjects[i], badgeCanvas.transform);

                    LOG($"Player {player.displayName} has additive role {role}, adding badge");
                }
            }

            // Check if the player has a staff info badge
            for (var i = 0; i < toggleableBadgeRoles.Length; i++)
            {
                var role = toggleableBadgeRoles[i];
                if (accountManager._GetBool(player.displayName, role))
                {
                    // Instantiate the badge
                    var badge = Instantiate(toggleableBadgeObjects[i], badgeCanvas.transform);
                    AddToggleableBadge(badge);

                    LOG($"Player {player.displayName} has toggleable role {role}, adding badge");
                }
            }

            // Enable the badge canvas
            badgeCanvas.SetActive(true);

            // Add the badge canvas to the list
            AddBadgeCanvas(player, badgeCanvas);
            updateHiddenBadgeOwnersIndex();
            setToggleableBadges(areToggleableBadgesToggled);
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            // Check if the player has a badge
            if (!HasBadge(player)) return;

            // Remove the badge
            RemoveBadgeCanvas(player);
        }

        public void toggleOwnBadge()
        {
            // Check if they are in the database
            if (!accountManager._IsOfficer(Networking.LocalPlayer))
            {
                // Disable badge toggles
                foreach (var toggle in showMyBadgeToggles)
                {
                    toggle.SetIsOnWithoutNotify(false);
                    toggle.interactable = false;
                }
                return;
            }

            // Remove or add badge by adding or removing the player from the hidden list
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
            var localPlayerBadge = GetBadge(Networking.LocalPlayer);
            if (!localPlayerBadge.activeInHierarchy)
            {
                LOG("Removing badge from hidden");
                removeFromHiddenPlayers(Networking.LocalPlayer.playerId);
            }
            else
            {
                LOG("Adding badge to hidden");
                addToHiddenPlayers(Networking.LocalPlayer.playerId);
            }
            
            // Set buttons to the correct state
            foreach (var toggle in showMyBadgeToggles) toggle.SetIsOnWithoutNotify(!localPlayerBadge.activeInHierarchy);

            // Serialize the data
            RequestSerialization();
            OnDeserialization();
        }

        public override void OnDeserialization()
        {
            // Update badge indexes
            updateHiddenBadgeOwnersIndex();

            // Update badge visibility
            for (var i = 0; i < _badgeOwners.Length; i++) _badgeObjects[i].SetActive(!_badgeIsHiddenOwnersIndex[i]);
        }

        private bool HasBadge(VRCPlayerApi player)
        {
            for (var i = 0; i < _badgeOwners.Length; i++)
                if (_badgeOwners[i].playerId == player.playerId)
                    return true;

            return false;
        }

        private GameObject GetBadge(VRCPlayerApi player)
        {
            for (var i = 0; i < _badgeOwners.Length; i++)
                if (_badgeOwners[i].playerId == player.playerId)
                    return _badgeObjects[i];

            return null;
        }

        private void updateHiddenBadgeOwnersIndex()
        {
            // Check which badge owners want to be hidden
            _badgeIsHiddenOwnersIndex = new bool[_badgeOwners.Length];

            for (var i = 0; i < _badgeOwners.Length; i++)
            {
                var badgeOwner = _badgeOwners[i];

                // Check if the player is in the hidden list
                _badgeIsHiddenOwnersIndex[i] = false;
                foreach (var hiddenPlayerID in _syncedHiddenPlayersID)
                    if (badgeOwner.playerId == hiddenPlayerID)
                        _badgeIsHiddenOwnersIndex[i] = true;
            }
        }

        public void ToggleToggleableBadge()
        {
            // Prevents multiple updates per frame
            if (Time.frameCount == _lastRefreshFrame) return;
            _lastRefreshFrame = Time.frameCount;

            LOG("Toggling toggleable badges");

            // Toggle the badge visibility
            setToggleableBadges(!areToggleableBadgesToggled);
        }
        
        public void ToggleAllBadges()
        {
            LOG("Toggling all badges");

            // Toggle the badge visibility
            ShowAllBadgesState(!badgeContainer.activeSelf);
        }
        
        private void ShowAllBadgesState(bool state)
        {
            // Just hide the badgeContainer
            badgeContainer.SetActive(state);
            
            // Set the toggles to the correct state
            foreach (var toggle in showAllBadgesToggles) toggle.SetIsOnWithoutNotify(state);
        }

        public void setToggleableBadges(bool state)
        {
            areToggleableBadgesToggled = state;

            LOG($"Setting toggleable badges to {state}");

            foreach (var badge in toggleableBadges)
            {
                badge.SetActive(state);
                LOG($"Setting badge {badge.name} to {state}");
            }

            // Set the toggles to the correct state
            foreach (var toggle in showToggleableBadgesToggles) toggle.SetIsOnWithoutNotify(state);
        }

        private void UpdatePlayerList()
        {
            // Prevents multiple updates per frame
            if (Time.frameCount == _lastRefreshFrame) return;
            _lastRefreshFrame = Time.frameCount;

            // Get all players
            _players = new VRCPlayerApi[0];

            if (_players == null || _players.Length != VRCPlayerApi.GetPlayerCount())
                _players = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];

            VRCPlayerApi.GetPlayers(_players);

            LOG("Player list updated, logged players: " + _players.Length);
        }

        // Good ol custom array functions
        // Love UdonSharp for this, give us array functions
        private void AddBadgeCanvas(VRCPlayerApi player, GameObject badgeCanvas)
        {
            // Create the new arrays
            var newBadgeOwners = new VRCPlayerApi[_badgeOwners.Length + 1];
            var newBadgeObjects = new GameObject[_badgeObjects.Length + 1];

            // Copy the old arrays
            for (var i = 0; i < _badgeOwners.Length; i++)
            {
                newBadgeOwners[i] = _badgeOwners[i];
                newBadgeObjects[i] = _badgeObjects[i];
            }

            // Add the new badge
            newBadgeOwners[newBadgeOwners.Length - 1] = player;
            newBadgeObjects[newBadgeObjects.Length - 1] = badgeCanvas;

            // Check if the arrays are good
            if (newBadgeOwners.Length != newBadgeObjects.Length || newBadgeOwners.Length != _badgeOwners.Length + 1)
            {
                LOG("Something went wrong at AddBadgeCanvas()", true);
                return;
            }

            // Replace the old arrays
            _badgeOwners = newBadgeOwners;
            _badgeObjects = newBadgeObjects;
        }

        private void RemoveBadgeCanvas(VRCPlayerApi player)
        {
            var index = -1;
            for (var i = 0; i < _badgeOwners.Length; i++)
                if (_badgeOwners[i].playerId == player.playerId)
                {
                    index = i;
                    break;
                }

            // If the player wasn't found, do nothing
            if (index == -1) return;

            // Destroy the badge
            Destroy(_badgeObjects[index].gameObject);

            // Create the new arrays
            var newBadgeOwners = new VRCPlayerApi[_badgeOwners.Length - 1];
            var newBadgeObjects = new GameObject[_badgeObjects.Length - 1];

            // Copy the old arrays, skipping the player
            var j = 0;
            for (var i = 0; i < _badgeOwners.Length; i++)
            {
                if (i == index) continue;
                newBadgeOwners[j] = _badgeOwners[i];
                newBadgeObjects[j] = _badgeObjects[i];
                j++;
            }

            // Before setting, check if the arrays are good
            if (newBadgeOwners.Length != newBadgeObjects.Length || newBadgeOwners.Length != _badgeOwners.Length - 1)
            {
                LOG("Something went wrong at RemoveBadgeCanvas()", true);
                return;
            }

            // Replace the old arrays
            _badgeOwners = newBadgeOwners;
            _badgeObjects = newBadgeObjects;
        }

        // Player toggle request array functions

        public void addToHiddenPlayers(int playerID)
        {
            // Create the new array
            var newHiddenPlayers = new int[_syncedHiddenPlayersID.Length + 1];

            // Copy the old array
            for (var i = 0; i < _syncedHiddenPlayersID.Length; i++) newHiddenPlayers[i] = _syncedHiddenPlayersID[i];

            // Add the new player
            newHiddenPlayers[newHiddenPlayers.Length - 1] = playerID;

            // Replace the old array
            _syncedHiddenPlayersID = newHiddenPlayers;
        }

        private void removeFromHiddenPlayers(int playerID)
        {
            var index = -1;
            for (var i = 0; i < _syncedHiddenPlayersID.Length; i++)
                if (_syncedHiddenPlayersID[i] == playerID)
                {
                    index = i;
                    break;
                }

            // If the player wasn't found, do nothing
            if (index == -1) return;

            // Create the new array
            var newHiddenPlayers = new int[_syncedHiddenPlayersID.Length - 1];

            // Copy the old array, skipping the player
            var j = 0;
            for (var i = 0; i < _syncedHiddenPlayersID.Length; i++)
            {
                if (i == index) continue;
                newHiddenPlayers[j] = _syncedHiddenPlayersID[i];
                j++;
            }

            // Replace the old array
            _syncedHiddenPlayersID = newHiddenPlayers;
        }

        // Toggleable badge array functions
        private void AddToggleableBadge(GameObject badge)
        {
            // Create the new array
            var newToggleableBadges = new GameObject[toggleableBadges.Length + 1];

            // Copy the old array
            for (var i = 0; i < toggleableBadges.Length; i++) newToggleableBadges[i] = toggleableBadges[i];

            // Add the new badge
            newToggleableBadges[newToggleableBadges.Length - 1] = badge;

            // Replace the old array
            toggleableBadges = newToggleableBadges;
        }

        public void nullCheckToggleableBadges()
        {
            // Remove all null entries from the array
            for (var i = 0; i < toggleableBadges.Length; i++)
                if (toggleableBadges[i] == null)
                    RemoveToggleableBadge(i);
        }

        private void RemoveToggleableBadge(int index)
        {
            // Create the new array
            var newToggleableBadges = new GameObject[toggleableBadges.Length - 1];

            // Copy the old array, skipping the badge
            var j = 0;
            for (var i = 0; i < toggleableBadges.Length; i++)
            {
                if (i == index) continue;
                newToggleableBadges[j] = toggleableBadges[i];
                j++;
            }

            // Replace the old array
            toggleableBadges = newToggleableBadges;
        }

        private void LOG(string message, bool error = false)
        {
            if (error)
                Debug.LogError($"<color=red><b>[Badge Manager]</b></color>{message}");
            else
                Debug.Log($"<color=green><b>[Badge Manager]</b></color>{message}");
        }

#if UNITY_EDITOR && !COMPILER_UDONSHARP
        private void OnValidate()
        {
            // Try to find the account manager
            if (accountManager == null) accountManager = FindObjectOfType<OfficerAccountManager>();

            UpdateBadgeToggles();
        }

        public void UpdateBadgeToggles()
        {
            // Try to find the buttons
            // Find all HiddenBadgeToggle components
            var hiddenBadgeToggles = FindObjectsOfType<HiddenBadgeToggle>();

            // Clear all lists
            showMyBadgeToggles = Array.Empty<Toggle>();
            showToggleableBadgesToggles = Array.Empty<Toggle>();
            showAllBadgesToggles = Array.Empty<Toggle>();

            var thisUdon = GetComponent<UdonBehaviour>();

            // Go through all the hidden badge toggles
            foreach (var hiddenBadgeToggle in hiddenBadgeToggles)
                switch (hiddenBadgeToggle.badgeToggleType)
                {
                    case BadgeToggleType.ShowMyBadge:
                        Array.Resize(ref showMyBadgeToggles, showMyBadgeToggles.Length + 1);
                        showMyBadgeToggles[^1] = hiddenBadgeToggle.GetComponent<Toggle>();
                        setupToggleButton(thisUdon, hiddenBadgeToggle.GetComponent<Toggle>(), nameof(toggleOwnBadge));
                        break;
                    case BadgeToggleType.ShowToggleableBadges:
                        Array.Resize(ref showToggleableBadgesToggles, showToggleableBadgesToggles.Length + 1);
                        showToggleableBadgesToggles[^1] = hiddenBadgeToggle.GetComponent<Toggle>();
                        setupToggleButton(thisUdon, hiddenBadgeToggle.GetComponent<Toggle>(), nameof(ToggleToggleableBadge));
                        break;
                    case BadgeToggleType.ShowAllBadges:
                        Array.Resize(ref showAllBadgesToggles, showAllBadgesToggles.Length + 1);
                        showAllBadgesToggles[^1] = hiddenBadgeToggle.GetComponent<Toggle>();
                        setupToggleButton(thisUdon, hiddenBadgeToggle.GetComponent<Toggle>(), nameof(ToggleAllBadges));
                        break;
                }
        }

        private static void setupToggleButton(UdonBehaviour toCall, Toggle button, string methodName)
        {
            // Get the udonbehaviour of this object
            var toCallUdon = toCall;

            // Clear all listeners
            var listeners = button.onValueChanged.GetPersistentEventCount();
            for (var i = 0; i < listeners; i++) UnityEventTools.RemovePersistentListener(button.onValueChanged, 0);

            var method = toCallUdon.GetType().GetMethod("SendCustomEvent",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var action =
                Delegate.CreateDelegate(typeof(UnityAction<string>), toCall, method) as
                    UnityAction<string>;
            UnityEventTools.AddStringPersistentListener(button.onValueChanged, action,
                methodName);
            // Save
            EditorUtility.SetDirty(button);
        }
#endif
    }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
    [CustomEditor(typeof(BadgeManager))]
    public class BadgeManagerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            var badgeManager = (BadgeManager)target;

            if (GUILayout.Button("Update Badge Toggles")) badgeManager.UpdateBadgeToggles();
        }
    }
#endif
}