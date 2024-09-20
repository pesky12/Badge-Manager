> [!WARNING]
> This repository is not a complete product ready for use. It is made public solely as a learning resource during development with expected future stable release if my motivation persists :).

# Badge Manager for VRChat

A (decently) powerful system designed for managing badges within VRChat. 
This system assigns staff/VIP/Specific members in-game badges dynamically for various roles using integration with **OfficerAccountManager**.

## ✨ Features
- **Dynamic Badge Assignment**: Automatically assigns badges based on user roles.
- **Toggleable Badges**: Staff members can toggle specific badges for display or hide them when needed.
- **Customizable Badges**: Configure different badge types, including role-based, additive, and toggleable badges.
- **Spam Prevention**: Prevents users from toggling badges too quickly to avoid spam.
- **Role Integration**: Works with **OfficerAccountManager** for streamlined officer and user role management.

---

## ⚙️ Installation

**TODO**

---

## 🛠️ Configuration

- **badgeCanvasPrefab**: The badge canvas prefab to be instantiated for each player that will badge icon elements use.
- **badgeOffset**: Adjust the positioning of badges relative to player avatars.
- **badgeRoles**: An array of roles ordered from highest to lowest, where only the highest in the database will get added/
- **additiveBadgeRoles**: Roles for assigning extra badges that get added to the player's badge list.
- **toggleableBadgeRoles**: Extra info list for badges that can be toggled locally. (Ex. Verification badges for security purposes)

---

## 🖥️ Usage

**TODO**

---

## 💡 Example Workflow

1. A player joins the world.
2. BadgeManager checks the player's role through `OfficerAccountManager`.
3. A badge is assigned based on the player's role, and any additive or toggleable badges are added if applicable.

---

## 📝 License
This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

**Made with 🐦 by Pesky**