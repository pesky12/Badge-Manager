using System.Diagnostics.CodeAnalysis;
using LoliPoliceDepartment.Utilities.AccountManager;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

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
        [SerializeField] GameObject badgeCanvasPrefab;
        [SerializeField] Vector3 badgeOffset;
        [SerializeField] bool logEnabled = true;
        
        // Badge Roles
        // This needs to be ordered from the highest rank to lowest
        [SerializeField] string[] badgeRoles = new string[0];
        [SerializeField] GameObject[] badgePrefabs = new GameObject[0];

        // Additive Badge Roles
        [SerializeField] string[] additiveBadgeRoles = new string[0];
        [SerializeField] GameObject[] additiveBadgeObjects = new GameObject[0];

        // Staff Info Badge Roles
        [SerializeField] string[] toggleableBadgeRoles = new string[0];
        [SerializeField] GameObject[] toggleableBadgeObjects = new GameObject[0];
        GameObject[] toggleableBadges = new GameObject[0];
        private bool areToggleableBadgesToggled = false;
        
        // Badge Owners
        private VRCPlayerApi[] _badgeOwners = new VRCPlayerApi[0];
        private GameObject[] _badgeObjects = new GameObject[0];
        
        // Timing
        private int _lastRefreshFrame = -1;
        
        // VRC Player data
        private VRCPlayerApi[] _players = new VRCPlayerApi[0];
        private VRCPlayerApi _localPlayer = null;
        
        // Player toggle request data
        [UdonSynced] private int[] _syncedHiddenPlayersID = new int[0];
        private int[] _locallyHiddenPlayersID = new int[0];
        private bool[] _badgeIsHiddenOwnersIndex = new bool[0];
        
        // Anti-spam
        private float localTimeStorage;
        private int localCounterStorage;

        private void Start()
        {
            // Load player data
            _localPlayer = Networking.LocalPlayer;
            UpdatePlayerList();
            
            accountManager.NotifyWhenInitialized(this, nameof(OnInitialized));
        }

        public void OnInitialized()
        {
            // Simulate player join for all players to populate the badge list
            foreach (var player in _players)
            {
                OnPlayerJoined(player);
            }
            
            // Sync up the badge hiding by simulating a Serialization request
            RequestSerialization();
        }

        private void LateUpdate()
        {
            // Cache clients head position
            Vector3 headPosition = _localPlayer.GetBonePosition(HumanBodyBones.Head);
            
            // Update badge locations for all players
            for (int i = 0; i < _badgeOwners.Length; i++)
            {
                // Don't update hidden badges
                // if (_badgeIsHiddenOwnersIndex[i]) continue;
                
                _badgeObjects[i].transform.position = _badgeOwners[i].GetBonePosition(HumanBodyBones.Head) + badgeOffset;
                
                // Make the badge face the player
                Vector3 lookAt = new Vector3(headPosition.x, headPosition.y, headPosition.z);
                _badgeObjects[i].transform.LookAt(lookAt);
            }
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            // Preflight
            if (!accountManager.isReady) return;
            if (HasBadge(player)) return;
            
            LOG($"Player {player.displayName} joined", false);
            LOG(accountManager._GetBool(player.displayName, "Triskel Developer").ToString(), false);
            
            if(!accountManager._IsOfficer(player.displayName)) return;
            
            LOG($"Player {player.displayName} joined and is in database, adding badge", false);
            
            // Create a canvas to add the badge to
            GameObject badgeCanvas = Instantiate(badgeCanvasPrefab, player.GetBonePosition(HumanBodyBones.Head), Quaternion.identity);
            
            // Check if the player has a role badge
            for (var i = 0; i < badgeRoles.Length; i++)
            {
                var role = badgeRoles[i];
                if (accountManager._GetBool(player.displayName, role))
                {
                    // Instantiate the badge
                    GameObject badge = Instantiate(badgePrefabs[i], badgeCanvas.transform);
                    
                    LOG($"Player {player.displayName} has role {role}, adding badge", false);

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
                    GameObject badge = Instantiate(additiveBadgeObjects[i], badgeCanvas.transform);
                    
                    LOG($"Player {player.displayName} has additive role {role}, adding badge", false);
                }
            }
            
            // Check if the player has a staff info badge
            for (var i = 0; i < toggleableBadgeRoles.Length; i++)
            {
                var role = toggleableBadgeRoles[i];
                if (accountManager._GetBool(player.displayName, role))
                {
                    // Instantiate the badge
                    GameObject badge = Instantiate(toggleableBadgeObjects[i], badgeCanvas.transform);
                    AddToggleableBadge(badge);
                    
                    LOG($"Player {player.displayName} has toggleable role {role}, adding badge", false);
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
            if (!accountManager._IsOfficer(Networking.LocalPlayer)) return;
            
            // Anti-spam, prevent rapid toggling within a 5-second window
            if (localTimeStorage == 0 || Time.time - localTimeStorage >= 5)
            {
                localTimeStorage = Time.time;
                localCounterStorage = 1;
            }
            else
            {
                if (++localCounterStorage > 3)
                {
                    LOG("Badge toggle spam detected, action blocked", true);
                    return;
                }
            }
            
            // Remove or add badge by adding or removing the player from the hidden list
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
            if (!GetBadge(Networking.LocalPlayer).activeInHierarchy)
            {
                LOG("Removing badge from hidden", false);
                removeFromHiddenPlayers(Networking.LocalPlayer.playerId);
            }
            else
            {
                LOG("Adding badge to hidden", false);
                addToHiddenPlayers(Networking.LocalPlayer.playerId);
            }

            // Serialize the data
            RequestSerialization();
            OnDeserialization();
        }

        public override void OnDeserialization()
        {
            // Update badge indexes
            updateHiddenBadgeOwnersIndex();
            
            // Update badge visibility
            for (int i = 0; i < _badgeOwners.Length; i++)
            {
                LOG($"Updating badge {i} visibility", false);
                _badgeObjects[i].SetActive(!_badgeIsHiddenOwnersIndex[i]);
            }
        }

        private bool HasBadge(VRCPlayerApi player)
        {
            for (int i = 0; i < _badgeOwners.Length; i++)
            {
                if (_badgeOwners[i].playerId == player.playerId)
                {
                    return true;
                }
            }

            return false;
        }
        
        private GameObject GetBadge(VRCPlayerApi player)
        {
            for (int i = 0; i < _badgeOwners.Length; i++)
            {
                if (_badgeOwners[i].playerId == player.playerId)
                {
                    return _badgeObjects[i];
                }
            }

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
                {
                    if (badgeOwner.playerId == hiddenPlayerID)
                    {
                        _badgeIsHiddenOwnersIndex[i] = true;
                    }
                }
            }
        }

        public void ToggleToggleableBadge()
        {
            // Prevents multiple updates per frame
            if (Time.frameCount == _lastRefreshFrame) return;
            _lastRefreshFrame = Time.frameCount;
            
            LOG("Toggling toggleable badges", false);
            
            // Toggle the badge visibility
            setToggleableBadges(!areToggleableBadgesToggled);
        }
        
        public void setToggleableBadges(bool state)
        {
            areToggleableBadgesToggled = state;
            
            LOG($"Setting toggleable badges to {state}", false);
            
            foreach (var badge in toggleableBadges)
            {
                badge.SetActive(state);
                LOG($"Setting badge {badge.name} to {state}", false);
            }
        }

        private void UpdatePlayerList()
        {
            // Prevents multiple updates per frame
            if (Time.frameCount == _lastRefreshFrame) return;
            _lastRefreshFrame = Time.frameCount;
            
            // Get all players
            _players = new VRCPlayerApi[0];

            if (_players == null || _players.Length != VRCPlayerApi.GetPlayerCount())
            {
                _players = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
            }
            
            VRCPlayerApi.GetPlayers(_players);
            
            LOG("Player list updated, logged players: " + _players.Length);
        }
        
        // Good ol custom array functions
        // Love UdonSharp for this, give us array functions
        private void AddBadgeCanvas(VRCPlayerApi player, GameObject badgeCanvas)
        {
            // Create the new arrays
            VRCPlayerApi[] newBadgeOwners = new VRCPlayerApi[_badgeOwners.Length + 1];
            GameObject[] newBadgeObjects = new GameObject[_badgeObjects.Length + 1];
            
            // Copy the old arrays
            for (int i = 0; i < _badgeOwners.Length; i++)
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
            int index = -1;
            for (int i = 0; i < _badgeOwners.Length; i++)
            {
                if (_badgeOwners[i].playerId == player.playerId)
                {
                    index = i;
                    break;
                }
            }

            // If the player wasn't found, do nothing
            if (index == -1) return;

            // Destroy the badge
            Destroy(_badgeObjects[index].gameObject);
            
            // Create the new arrays
            VRCPlayerApi[] newBadgeOwners = new VRCPlayerApi[_badgeOwners.Length - 1];
            GameObject[] newBadgeObjects = new GameObject[_badgeObjects.Length - 1];
            
            // Copy the old arrays, skipping the player
            int j = 0;
            for (int i = 0; i < _badgeOwners.Length; i++)
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
            int[] newHiddenPlayers = new int[_syncedHiddenPlayersID.Length + 1];
            
            // Copy the old array
            for (int i = 0; i < _syncedHiddenPlayersID.Length; i++)
            {
                newHiddenPlayers[i] = _syncedHiddenPlayersID[i];
            }
            
            // Add the new player
            newHiddenPlayers[newHiddenPlayers.Length - 1] = playerID;
            
            // Replace the old array
            _syncedHiddenPlayersID = newHiddenPlayers;
        }
        
        void removeFromHiddenPlayers(int playerID)
        {
            int index = -1;
            for (int i = 0; i < _syncedHiddenPlayersID.Length; i++)
            {
                if (_syncedHiddenPlayersID[i] == playerID)
                {
                    index = i;
                    break;
                }
            }

            // If the player wasn't found, do nothing
            if (index == -1) return;

            // Create the new array
            int[] newHiddenPlayers = new int[_syncedHiddenPlayersID.Length - 1];
            
            // Copy the old array, skipping the player
            int j = 0;
            for (int i = 0; i < _syncedHiddenPlayersID.Length; i++)
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
            GameObject[] newToggleableBadges = new GameObject[toggleableBadges.Length + 1];
            
            // Copy the old array
            for (int i = 0; i < toggleableBadges.Length; i++)
            {
                newToggleableBadges[i] = toggleableBadges[i];
            }
            
            // Add the new badge
            newToggleableBadges[newToggleableBadges.Length - 1] = badge;
            
            // Replace the old array
            toggleableBadges = newToggleableBadges;
        }
        
        public void nullCheckToggleableBadges()
        {
            // Remove all null entries from the array
            for (int i = 0; i < toggleableBadges.Length; i++)
            {
                if (toggleableBadges[i] == null)
                {
                    RemoveToggleableBadge(i);
                }
            }
        }
        
        private void RemoveToggleableBadge(int index)
        {
            // Create the new array
            GameObject[] newToggleableBadges = new GameObject[toggleableBadges.Length - 1];
            
            // Copy the old array, skipping the badge
            int j = 0;
            for (int i = 0; i < toggleableBadges.Length; i++)
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
    }

}