## [0.8.10](https://github.com/LaurensDeV/KSArmory/compare/v0.8.9...v0.8.10) (2026-08-05)

### Features

* **sim:** keep a system's settings inside the save it belongs to ([5afd27f](https://github.com/LaurensDeV/KSArmory/commit/5afd27fa737ee5b7646031fefbf58b513a519b11))
* **ui:** let a system's remembered settings be reset ([8463de5](https://github.com/LaurensDeV/KSArmory/commit/8463de552f742cc559480259eb7e659cdc389490))

### Fixes

* **rounds:** floor how small a warhead effect is drawn ([c08055d](https://github.com/LaurensDeV/KSArmory/commit/c08055d6f28cddfe55212d6617d971fd3fb7e59c))
* **rounds:** put the burst where the round is drawn, not where it is ([5a98fea](https://github.com/LaurensDeV/KSArmory/commit/5a98fea9ce214d59b4973514a59798584c03a918))
* **rounds:** stop the airburst compounding two reductions to nothing ([7414605](https://github.com/LaurensDeV/KSArmory/commit/7414605e68d3ccdaea059bfa2e6bea38b97c483d))
* **sim:** opening a save is a read, not a write ([1b96bad](https://github.com/LaurensDeV/KSArmory/commit/1b96bada971d96ab6a1323ce85f158d9b2964900))

### Internal

* **ui:** drop the Kittens pane and the Session group with it ([3b016bc](https://github.com/LaurensDeV/KSArmory/commit/3b016bca823a265b92fc6ed64efe56e5c35e9570))
* **ui:** split the panel into Ksa/Ui/ along the pane seam ([51b0a6f](https://github.com/LaurensDeV/KSArmory/commit/51b0a6fe84efbe2aed025eebe697f6341c805c5c))

## [0.8.9](https://github.com/LaurensDeV/KSArmory/compare/v0.8.8...v0.8.9) (2026-08-05)

### Features

* **sim:** scope each system's settings to the save it was set in ([990f015](https://github.com/LaurensDeV/KSArmory/commit/990f015f8ff1650ed301554eb386e14b117a6377))

## [0.8.8](https://github.com/LaurensDeV/KSArmory/compare/v0.8.7...v0.8.8) (2026-08-05)

### Features

* **model:** give the kitten a shoulder cannon ([35e9404](https://github.com/LaurensDeV/KSArmory/commit/35e94040beb8e9b103ec7b4c94635404ed4325ae))
* **rounds:** draw the fallback smoke as soft billboards, not spheres ([57ca05d](https://github.com/LaurensDeV/KSArmory/commit/57ca05d7362001e88a3d2a8ed6c2c92a6bb7737b))
* **rounds:** give a warhead a charge, and read its reach off it ([56ea053](https://github.com/LaurensDeV/KSArmory/commit/56ea0531ecf9f051e30868b8faf1ec4afe732580))
* **rounds:** let the fireball linger instead of blinking ([763f244](https://github.com/LaurensDeV/KSArmory/commit/763f244f63a328e39a3e394491a15a17edba6dcd))
* **rounds:** render the smoke volumetrically instead of as spheres ([d8b9d92](https://github.com/LaurensDeV/KSArmory/commit/d8b9d9284ee0029eeddf0853396a00c3e0f1948c))
* **rounds:** show a fireball where the warhead goes off ([67b6eb2](https://github.com/LaurensDeV/KSArmory/commit/67b6eb2623c7a90a70b5d7e51d2c78fe8e1649bf))
* **sim:** classify a camera as its own role, not a sensor ([dd8110c](https://github.com/LaurensDeV/KSArmory/commit/dd8110cec23dedea37614efbc996127d4aa4081b))
* **sim:** give every weapons system its own battery ([ab033e0](https://github.com/LaurensDeV/KSArmory/commit/ab033e01739495e38c4589634ed48e5493055c02))
* **sim:** keep each system's settings between sessions ([c7b3626](https://github.com/LaurensDeV/KSArmory/commit/c7b3626bbe85ed5efdafa7e632c884f647bc00d9))
* **sim:** report the roles a prefab carries inside itself ([3a713c2](https://github.com/LaurensDeV/KSArmory/commit/3a713c288bd6e3d5c451724739f74e05e2ada143))
* **sim:** survey a craft for the weapons installed on it ([764e8b6](https://github.com/LaurensDeV/KSArmory/commit/764e8b624a2e14be3e4b23773f35312c245aaaf2))
* **turret:** aim the launcher with the mouse, from a panel toggle ([aa9fff7](https://github.com/LaurensDeV/KSArmory/commit/aa9fff72d8b59946db6a1835edaf8e5ee1204a29))
* **ui:** bracket every weapons system on screen, with range on hover ([9de62e1](https://github.com/LaurensDeV/KSArmory/commit/9de62e1aac2758d0dd422c577197d27324d76a86))
* **ui:** click the world to set off a warhead there ([098f5f1](https://github.com/LaurensDeV/KSArmory/commit/098f5f1825fc3a49a5a855fb600a4d4c009eef7b))
* **ui:** draw the world overlay for one system, and add a master switch ([2ad96bc](https://github.com/LaurensDeV/KSArmory/commit/2ad96bc429f7fa7e91a6db6e20030e647bd8426a))
* **ui:** drop a system's marker once it fills the view ([f4fc27a](https://github.com/LaurensDeV/KSArmory/commit/f4fc27a5139b052af224b85897b542623b168337))
* **ui:** go to a weapons system from the list ([9fce592](https://github.com/LaurensDeV/KSArmory/commit/9fce5920b5042f216b83324149d8251e27d19aa4))
* **ui:** group a system's components by role, foldable ([9a08d18](https://github.com/LaurensDeV/KSArmory/commit/9a08d1854300ad2dfc33203549287c371cdb1c04))
* **ui:** hold the main view on a system, from where you are ([be20b65](https://github.com/LaurensDeV/KSArmory/commit/be20b652b64ecc37acaabae33c87a6050864bc8e))
* **ui:** let go of the view the moment the player moves it ([c37f82c](https://github.com/LaurensDeV/KSArmory/commit/c37f82c7da019c1257f7154358826c5ba15f6248))
* **ui:** let the panel arm a kitten, and say which character it wears ([ed16726](https://github.com/LaurensDeV/KSArmory/commit/ed16726af9ee9d49464a6046248246fe1755459c))
* **ui:** let the weapons-system markers be turned off ([69d4548](https://github.com/LaurensDeV/KSArmory/commit/69d45489dae0ae1e1b4af4fb14bc61b2341f1198))
* **ui:** look at a system from the list, and mark it ([2220295](https://github.com/LaurensDeV/KSArmory/commit/2220295114c89e8f21ab2ac93f47dd97f342b05e))
* **ui:** mark a system the planet is in the way of ([5b8a933](https://github.com/LaurensDeV/KSArmory/commit/5b8a933433dd31a5cbe3a4d370c0e4f3977515c7))
* **ui:** nudge the view towards a system instead of seizing it ([87b2b64](https://github.com/LaurensDeV/KSArmory/commit/87b2b6463d71d953ac9529a3930ce299042894ad))
* **ui:** one row per system, and a window per system to manage it ([90e9b64](https://github.com/LaurensDeV/KSArmory/commit/90e9b641e9aa0347a841c1b03c350e9e434b740f))
* **ui:** pick a craft anywhere on it, and place onto it squarely ([0f81693](https://github.com/LaurensDeV/KSArmory/commit/0f816933424c3f7e8f090f15c0140b25ac94fd3a))
* **ui:** pick a craft up and set it down somewhere else ([6f9242e](https://github.com/LaurensDeV/KSArmory/commit/6f9242e1062f6d6ab6db47b408b82621c23e7b52))
* **ui:** pick which system the panel configures, and move IFF onto it ([6ce0505](https://github.com/LaurensDeV/KSArmory/commit/6ce0505fd9fd603462b77d7827b577dfaedfe68c))
* **ui:** pin a system's label from the panel, and drop the ping ([4f99c8b](https://github.com/LaurensDeV/KSArmory/commit/4f99c8bc97b249387c3eda84d7406d9567e3b7b5))
* **ui:** point an edge arrow at a system that is out of view ([660a43a](https://github.com/LaurensDeV/KSArmory/commit/660a43a0c8280b3c38efd6170d7e9400bba3670a))
* **ui:** put a lock on a marker's label to keep it up ([0b205f2](https://github.com/LaurensDeV/KSArmory/commit/0b205f2c8740df00e5f7700d50e51ef95f16eb20))
* **ui:** put the world overlay behind one debug switch, off by default ([0e73086](https://github.com/LaurensDeV/KSArmory/commit/0e73086a038de59d67014511c402af06217129a4))
* **ui:** report the character a flown kitten is actually wearing ([ac27609](https://github.com/LaurensDeV/KSArmory/commit/ac276090cc04b83a4c88fd14663fa85cca07f943))
* **ui:** ring the craft under the cursor, and land on the terrain ([0b3a302](https://github.com/LaurensDeV/KSArmory/commit/0b3a302dbea2507344cd2c966c905bb486d9b22e))
* **ui:** say why the battery is holding fire ([8c5d03c](https://github.com/LaurensDeV/KSArmory/commit/8c5d03cd7f9dc0be6d3200052a42fdbd9213cda5))
* **ui:** show which link of the armed-kitten chain resolved ([6504ed9](https://github.com/LaurensDeV/KSArmory/commit/6504ed9f46dffe8f126f9b27937f31b797d18262))

### Fixes

* **model:** allow the gun's embedded material to be used ([56075de](https://github.com/LaurensDeV/KSArmory/commit/56075dedfedc9c43ad7ab751dd2b7a9810ff7539))
* **model:** drop AllowEmbedded, whose stated reason was wrong ([6b23d32](https://github.com/LaurensDeV/KSArmory/commit/6b23d321309c32b490d4325d3a464d7e4ed254db))
* **model:** export the kitten gun in character space, not metres ([2b9ae32](https://github.com/LaurensDeV/KSArmory/commit/2b9ae323f5c07240d42c377b4a6425ff13aede2b))
* **model:** load the character XML, and stop the panel crashing the game ([1c1bc28](https://github.com/LaurensDeV/KSArmory/commit/1c1bc28c795dca0d2d5e0cc62b2990295feddadd))
* **model:** permute the gun into rig axes, so it stops pointing sideways ([87fa3bf](https://github.com/LaurensDeV/KSArmory/commit/87fa3bf4c379ef703457b805e740709d0353880d))
* **model:** reference the gun attachment as the deserialiser reads it ([f74e2ac](https://github.com/LaurensDeV/KSArmory/commit/f74e2acb28bd378db720768423b470c361569ec4))
* **rounds:** make the burst radial, not a cone in a fixed direction ([0fc2e2e](https://github.com/LaurensDeV/KSArmory/commit/0fc2e2e3e5292f8c16464883376d0f2068136b3b))
* **rounds:** nest the burst stages inline rather than by Id ([fad77b0](https://github.com/LaurensDeV/KSArmory/commit/fad77b0f380ec630f3d07d3f46fa9f9ee52e7b64))
* **rounds:** ship a fireball that draws without screen-space particles ([a472e98](https://github.com/LaurensDeV/KSArmory/commit/a472e980004831e6a4cb9492f42e88a4428e1b39))
* **rounds:** stop the whole burst falling out of the sky ([b616ff8](https://github.com/LaurensDeV/KSArmory/commit/b616ff83c3d3f33aa0993b79b0c1490ebf5d5f35))
* **sim:** let the warp stand-down budget survive a good frame ([186ac0a](https://github.com/LaurensDeV/KSArmory/commit/186ac0a59abe768b03b2193103509edb495945db))
* **sim:** stop the warp hold oscillating and fighting the player ([82a8eb5](https://github.com/LaurensDeV/KSArmory/commit/82a8eb5a823a49815397acf59ce1918e586d1d9b))
* **ui:** build the cursor ray from a single camera ([83eff0e](https://github.com/LaurensDeV/KSArmory/commit/83eff0e3c86b8e3183a2b913d82e3b13ac27c3e7))
* **ui:** drive the orbit view's angles, not the controller's copy ([6c67345](https://github.com/LaurensDeV/KSArmory/commit/6c673455872bac7dc0619f6a29409090626f4b25))
* **ui:** put the landing marker on the pad rather than under it ([6926608](https://github.com/LaurensDeV/KSArmory/commit/6926608931041619bb39014941dfdb462837f25d))
* **ui:** report the kitten off the controlled vehicle, not the platform ([e1d60ed](https://github.com/LaurensDeV/KSArmory/commit/e1d60ed43f4e4fc815f860903b7a457acb6213f7))
* **ui:** scale the cursor into framebuffer pixels, not viewport pixels ([a8a086d](https://github.com/LaurensDeV/KSArmory/commit/a8a086dd6ebb1f1c7eab6e7852986d555118a454))
* **ui:** solve the placement point on the cursor ray, not above it ([fbf5183](https://github.com/LaurensDeV/KSArmory/commit/fbf518354d9e38224673a26a0491ad55dbef904a))
* **ui:** unfollow before Fixed, which was crashing the game ([a16e6e3](https://github.com/LaurensDeV/KSArmory/commit/a16e6e3a6a29cceadb2970f9f02c78f2501e5c27))
* **ui:** watch a system by following it, not by rotating the camera ([721cb9b](https://github.com/LaurensDeV/KSArmory/commit/721cb9b8c07b8cf280c772799cc5c5564c23e499))

### Internal

* **sim:** make BallisticLead difference its own inputs ([34a08e2](https://github.com/LaurensDeV/KSArmory/commit/34a08e24fa15953e258d09b5fc844f048834334b)), closes [#4](https://github.com/LaurensDeV/KSArmory/issues/4)
* **sim:** split a battery's own settings out of Config ([2cae242](https://github.com/LaurensDeV/KSArmory/commit/2cae2422300e597c9f85c6f138846429921f6ee9))
* **ui:** make the survey a system's component list ([4ab5cb6](https://github.com/LaurensDeV/KSArmory/commit/4ab5cb68e31e94308a03dad0074e056446cb46bb))
* **ui:** open on the weapons systems, not the craft in view ([def5f33](https://github.com/LaurensDeV/KSArmory/commit/def5f339e5840554eed1b061afc2371078159dba))
* **ui:** open panes with buttons rather than tick boxes ([c80e6ac](https://github.com/LaurensDeV/KSArmory/commit/c80e6ac71b1ac1936f356668a77ea175cd841ca7))
* **ui:** say "not crewed" rather than naming the mod's internals ([1e3317e](https://github.com/LaurensDeV/KSArmory/commit/1e3317e0b216afd6833eab2894a845a2638ffc87))
* **ui:** split the panel into a main window and pop-out panes ([2bac915](https://github.com/LaurensDeV/KSArmory/commit/2bac9158f88a8fe221a91f7d3d8c236315062134))

### Documentation

* correct MODULARITY.md and make it cite symbols ([b79523e](https://github.com/LaurensDeV/KSArmory/commit/b79523e69bda44969f09b9585ba5dce76d55a361))
* record that a character attachment cannot be aimed ([e0347b5](https://github.com/LaurensDeV/KSArmory/commit/e0347b57c3aef619512614907cb2b532534507e3))
* record that a kitten cannot be launched from a vehicle save ([1d213a3](https://github.com/LaurensDeV/KSArmory/commit/1d213a35190ce3197bdf7f0c9d25c56ce781bb57))
* record two more character-attachment traps ([7d36df8](https://github.com/LaurensDeV/KSArmory/commit/7d36df88056fd2ea9a4efbe14d6a7afc1c1b943c))

## [0.8.7](https://github.com/LaurensDeV/KSArmory/compare/v0.8.6...v0.8.7) (2026-08-05)

### Features

* **sim:** hold timewarp down while rounds are in the air ([2e42236](https://github.com/LaurensDeV/KSArmory/commit/2e4223615bde076ba0c1b8b1f5ff4836fdeed276))

### Fixes

* **sim:** stop the cannon and the missiles fighting over one bearing ([7a09938](https://github.com/LaurensDeV/KSArmory/commit/7a09938abd73a5b8f6f9af4bef7aef6d4b89376c))

### Internal

* stop hardcoding one weapon system; fix two comments ([865e232](https://github.com/LaurensDeV/KSArmory/commit/865e23278e0a7434509f702962310aefe52a7c5b))

### Documentation

* repair the drift the audit found in CLAUDE.md ([b45a6cf](https://github.com/LaurensDeV/KSArmory/commit/b45a6cf90f122ec89dcde9f411528bab855a2f9d))

## [0.8.6](https://github.com/LaurensDeV/KSArmory/compare/v0.8.5...v0.8.6) (2026-08-05)

### Fixes

* **guns:** stop a burst outliving its lock from crashing fire control ([aab4c57](https://github.com/LaurensDeV/KSArmory/commit/aab4c5717c67d5b8c474914a8d28d5c3d07ef8d8))

### Documentation

* confirm the retarget in flight, and round bodies at 79 km ([09e0a19](https://github.com/LaurensDeV/KSArmory/commit/09e0a19cb9447febf6b571a6ed430de93d72d388))

## [0.8.5](https://github.com/LaurensDeV/KSArmory/compare/v0.8.4...v0.8.5) (2026-08-05)

### Build and packaging

* **ksa:** retarget 2026.8.5.5168 ([80fb957](https://github.com/LaurensDeV/KSArmory/commit/80fb957b6ad54dd0d6e6d348b300407b79255139))

### Documentation

* record that the retarget has not been flown ([da83bf8](https://github.com/LaurensDeV/KSArmory/commit/da83bf8de75e154e11df05e801461c0da6dfb596))

## [0.8.4](https://github.com/LaurensDeV/KSArmory/compare/v0.8.3...v0.8.4) (2026-08-05)

### Features

* **ui:** make teams and IFF reachable ([7c15b2c](https://github.com/LaurensDeV/KSArmory/commit/7c15b2c78a737ba27be23a48eb1174050a78eb71))

### Fixes

* **sim:** report the simulated time the frame hook throws away ([93c7e01](https://github.com/LaurensDeV/KSArmory/commit/93c7e01dd28e395ae3901ff9da64ebf5de90c457))

### Documentation

* lead the README with the mod rather than with the Pantsir ([a8e0253](https://github.com/LaurensDeV/KSArmory/commit/a8e0253e08d68e44adc367784d42d618946a3115))
* mark the model repairs confirmed in flight ([44fd7a8](https://github.com/LaurensDeV/KSArmory/commit/44fd7a89aac2531d12bafd2ce523d3aa58e3f294))
* record the August audit and its ranked backlog ([7cc7f48](https://github.com/LaurensDeV/KSArmory/commit/7cc7f481c92d9e79aa4991305545bf2f5f54214e))

## [0.8.3](https://github.com/LaurensDeV/KSArmory/compare/v0.8.2...v0.8.3) (2026-08-04)

### Features

* **rounds:** fit the 30 mm cannon, and an optical head to watch through ([cd0c721](https://github.com/LaurensDeV/KSArmory/commit/cd0c72142152acf9e6b5a6179ba0b7f273894d10))

### Fixes

* **model:** repair the pod frame, the turntable and the gun sponsons ([86a0784](https://github.com/LaurensDeV/KSArmory/commit/86a0784a37a9ae2a0963fd7b8ce319bd1780ca2f))
* **ui:** name the files the mod actually writes and ships ([e7a0384](https://github.com/LaurensDeV/KSArmory/commit/e7a03843bbea68244d0d6e840f9aa03c3cccd565))

### Documentation

* record what is blocked on KSA, and the atlas loader's contract ([48138ec](https://github.com/LaurensDeV/KSArmory/commit/48138ec2d6bba585d9f012349c3e429f8ec40317))

## [0.8.2](https://github.com/LaurensDeV/KSArmory/compare/v0.8.1...v0.8.2) (2026-08-04)

### Features

* **turret:** elevate the cannon with the launcher ([8035fba](https://github.com/LaurensDeV/KSArmory/commit/8035fbab33156ebaec40f7530cf904ac5f928a9e))

### Fixes

* **model:** mount the cannon on sponsons and give them daylight ([1fc641c](https://github.com/LaurensDeV/KSArmory/commit/1fc641c0b05a7a3cc82d72c6ee203972700770ca))
* **model:** place the cannon where their mesh was recentred ([a9682b6](https://github.com/LaurensDeV/KSArmory/commit/a9682b62a9a03012175ade89ab2dc9beb487161b))
* **model:** raise the cannon clear of the turret body ([14668ab](https://github.com/LaurensDeV/KSArmory/commit/14668ab08109d728da85679cd83acaec0357c9b7))
* **model:** unbury the cannon and give them their own body ([371866c](https://github.com/LaurensDeV/KSArmory/commit/371866c5d9cd50732d38e08df8450758e1b753c3))

### Documentation

* correct the versioning docs to match what the config now does ([f2444b7](https://github.com/LaurensDeV/KSArmory/commit/f2444b716c1c65168afb746bdf1a6fe3dff1002b))
* hold the copyright as KSArmory, and repair the changelog links ([bc57e39](https://github.com/LaurensDeV/KSArmory/commit/bc57e39de3923de9a74d1ec0c0138186b90edb08))
* write down what a different mod loader would have to provide ([26f434a](https://github.com/LaurensDeV/KSArmory/commit/26f434a1e158d72ea0584197d1fd0986696e2d9b))

## [0.8.1](https://github.com/LaurensDeV/KSArmory/compare/v0.8.0...v0.8.1) (2026-08-04)

### Features

* **radar:** let teams form coalitions rather than only mine-versus-all ([bf45225](https://github.com/LaurensDeV/KSArmory/commit/bf452256b3e9f14fa284998442876b89a3a8d6a2))

### Build and packaging

* cut patches for features too, and tag minors by hand ([7e5afe6](https://github.com/LaurensDeV/KSArmory/commit/7e5afe611e3974f98878dda07ec3103fafa4cc8f))
* name the assembly and its assets KSArmory too ([527520d](https://github.com/LaurensDeV/KSArmory/commit/527520dfcd7632914572bad79b8074fc67355a96))
* rename the in-game panel to KSArmory ([0f3177d](https://github.com/LaurensDeV/KSArmory/commit/0f3177d59b02961191544a3f62b64f5ea0b5600a))
* rename the project to KSArmory ([a3576b8](https://github.com/LaurensDeV/KSArmory/commit/a3576b8ccd2eaf76c8ea6de50981d94fe969e5d8))

### Documentation

* commits carry no attribution trailer ([e7e6038](https://github.com/LaurensDeV/KSArmory/commit/e7e6038040d7fcfa47fdfac7b65ad3c5ed51c6b8))
* feat means observable in the archive, not merely enabled ([4ecdef3](https://github.com/LaurensDeV/KSArmory/commit/4ecdef3db331fd556a89bb6b71a3f53d0a01a8bf))

## [0.8.0](https://github.com/LaurensDeV/KSArmory/compare/v0.7.0...v0.8.0) (2026-08-04)

### Features

* **radar:** identify contacts by team before engaging them ([94da4d8](https://github.com/LaurensDeV/KSArmory/commit/94da4d8e74be55187b4cd037ee8f0127041f7ca7))
* **rounds:** fly through any medium, including water ([8dde3f7](https://github.com/LaurensDeV/KSArmory/commit/8dde3f70bb4e03ec61a64c39e566930b5d015c69))
* **sim:** decouple magazine depth from tube count ([82a1e0b](https://github.com/LaurensDeV/KSArmory/commit/82a1e0ba70c6ee3b50dd17c7d95fad25d16b1e16))
* **sim:** let a round aim at a part or a point, not only a craft ([4e83b23](https://github.com/LaurensDeV/KSArmory/commit/4e83b234853f45165c40536df2891dad9a5abd0f))

### Documentation

* audit modularity against torpedoes, RPGs, aircraft and submarines ([aab95dd](https://github.com/LaurensDeV/KSArmory/commit/aab95ddeb3aaf75dc3f9eba33a7f1fdcc0970184))
* comments state the fact, not the history ([59b6cf0](https://github.com/LaurensDeV/KSArmory/commit/59b6cf0dce2969077fbe92f69a9525b87433d600))
* record what the four changes unblocked, and what is still open ([31349fa](https://github.com/LaurensDeV/KSArmory/commit/31349fa19b4e967426a1a8969ce3a7673d8989d9))
* strip history and narrative from comments ([a7ed136](https://github.com/LaurensDeV/KSArmory/commit/a7ed13633a759ed856f929a58979a686f4f483fb))
* write down the rules for comments and keeping docs true ([85c8ae6](https://github.com/LaurensDeV/KSArmory/commit/85c8ae63c99f96dd5e824cce1c85f05f1a568424))

## [0.7.0](https://github.com/LaurensDeV/KSArmory/compare/v0.6.0...v0.7.0) (2026-08-04)

### Features

* **radar:** let a sensor choose where its search cone points ([c184fb9](https://github.com/LaurensDeV/KSArmory/commit/c184fb9bc7cd0201cad46ba86c3ddff490f5d6cf))
* **rounds:** scale drag by air density so vacuum launches work ([da005cb](https://github.com/LaurensDeV/KSArmory/commit/da005cb21e5fc037505ae944b530beab735e97c9))
* **sim:** give each tube its own launch direction ([3a6416b](https://github.com/LaurensDeV/KSArmory/commit/3a6416b71f59f5ba73bc16987c1f5adac4093da8))
* **ui:** add slow-motion simulation speed buttons ([90f1c80](https://github.com/LaurensDeV/KSArmory/commit/90f1c8011c0d76a7a540e2418ca0d0995f1fcd2a))

### Fixes

* **rounds:** aim at the target's own instant, not one step ahead of it ([9ed1b06](https://github.com/LaurensDeV/KSArmory/commit/9ed1b0649c059df617282c6f091f28f0bb8da7ae))
* **rounds:** draw rounds from the frame that simulated them ([e7548c1](https://github.com/LaurensDeV/KSArmory/commit/e7548c145f0ef4ace3b560d7d5b2efaac8bfa0bf))
* **rounds:** measure the drawn offset against its own frame's platform ([bea84a6](https://github.com/LaurensDeV/KSArmory/commit/bea84a6fd7ebf1826d094937626c26b011a3eeeb))
* **rounds:** measure the tube muzzle from the centre of mass ([589c49b](https://github.com/LaurensDeV/KSArmory/commit/589c49b44b9b4948d779599c1126991d4236b5c8))
* **rounds:** seat every body so none can flash at the vehicle origin ([4140846](https://github.com/LaurensDeV/KSArmory/commit/41408461139f57038b2fd0373406aeeea14ef24a))
* **rounds:** stop a launching round pointing along Earth's orbit ([6ae0753](https://github.com/LaurensDeV/KSArmory/commit/6ae07530643946c32392c538fd8b04ae39f0ef15))
* **rounds:** track loaded tubes instead of deriving them from ammo ([628d57a](https://github.com/LaurensDeV/KSArmory/commit/628d57afe0cd3a7909144cbd71e9fb8172559c73))
* **sim:** join KSA's vehicle solvers before destroying a target ([e3be8ea](https://github.com/LaurensDeV/KSArmory/commit/e3be8ea11327e376278bb18618c7a706410c9f6c))
* **sim:** step on what the engine applied, not on the pause flag ([48d7ef6](https://github.com/LaurensDeV/KSArmory/commit/48d7ef6149afb6c9a367238c123f790f19d8bbd4))
* **testtarget:** spawn kittens as KittenEva so they can be flown ([770244d](https://github.com/LaurensDeV/KSArmory/commit/770244df8845ac5309445d1227e16eac66167f25))

### Internal

* **sim:** lift the launcher's tube geometry out of LauncherPart ([90a892f](https://github.com/LaurensDeV/KSArmory/commit/90a892fcd7a4d6449afd980f76dd3334de267d1c))
* **sim:** lift the sim-step dedup into a testable gate ([c78d541](https://github.com/LaurensDeV/KSArmory/commit/c78d541520c1a96109b2104dc43a11660ab099f1))
* **sim:** lift tube bookkeeping into a testable Magazine ([3ca5ba1](https://github.com/LaurensDeV/KSArmory/commit/3ca5ba178ec0a27adf343007664be510a1f55156))
* **sim:** make the projectile an abstraction, not one class ([df18e45](https://github.com/LaurensDeV/KSArmory/commit/df18e45b4c32ec1f242f63f5831239489f0c2359))

### Documentation

* correct the modularity audit after the extraction moved it ([159bd46](https://github.com/LaurensDeV/KSArmory/commit/159bd46ab1971fe357b0284d4249f82b0ddcdc96))
* record how far the weapon-system split generalises ([4ded6a3](https://github.com/LaurensDeV/KSArmory/commit/4ded6a390fb50d1415e5a8ef3540024ee3ea8c1c))

## [0.6.0](https://github.com/LaurensDeV/KSArmory/compare/v0.5.2...v0.6.0) (2026-08-03)

### Features

* **model:** fold clipped-delta fins out after launch ([4c2062d](https://github.com/LaurensDeV/KSArmory/commit/4c2062dfc22733355bde035bc39c1b3606649983))
* **rounds:** seat loaded rounds in their tubes ([01678fa](https://github.com/LaurensDeV/KSArmory/commit/01678fa21e7de101f27a8b37c6716544bd0faf83))
* **ui:** put the tube markers behind a debug toggle ([19b7940](https://github.com/LaurensDeV/KSArmory/commit/19b794012ba24f77cea41307c73d6ccaab1db00f))

### Documentation

* **model:** record the three traps that only bite hand-built meshes ([980a285](https://github.com/LaurensDeV/KSArmory/commit/980a285d9f0e64753b03dd30d771bb9a8395960f))

## [0.5.2](https://github.com/LaurensDeV/KSArmory/compare/v0.5.1...v0.5.2) (2026-08-03)

### Fixes

* **sim:** step on this frame's interval, expressed in simulated seconds ([b6ad885](https://github.com/LaurensDeV/KSArmory/commit/b6ad885d5ba77fb568cb76d8b41e47ab54fc12be))

## [0.5.1](https://github.com/LaurensDeV/KSArmory/compare/v0.5.0...v0.5.1) (2026-08-03)

### Fixes

* **draw:** step on the frame delta the platform sample moved over ([05dc512](https://github.com/LaurensDeV/KSArmory/commit/05dc512a33f7639cb827262bef1220ac0acc080f))

## [0.5.0](https://github.com/LaurensDeV/KSArmory/compare/v0.4.4...v0.5.0) (2026-08-03)

### Features

* **radar:** engage only what the round can actually reach ([585b318](https://github.com/LaurensDeV/KSArmory/commit/585b3182c7656de19562f93080c8b94205423bef))
* **rounds:** fly the 57E6 as the command-guided weapon it is ([9060854](https://github.com/LaurensDeV/KSArmory/commit/9060854d260ddaab5f7739e447782c5381b21867))

## [0.4.4](https://github.com/LaurensDeV/KSArmory/compare/v0.4.3...v0.4.4) (2026-08-03)

### Fixes

* **rounds:** accumulate travel rather than difference ecliptic positions ([c17735c](https://github.com/LaurensDeV/KSArmory/commit/c17735c3e1fc0b37d62a9c7980f4ae9ba4cc6aa7))

## [0.4.3](https://github.com/LaurensDeV/KSArmory/compare/v0.4.2...v0.4.3) (2026-08-03)

### Fixes

* **rounds:** stop extrapolating the platform a frame past where it is ([b422d39](https://github.com/LaurensDeV/KSArmory/commit/b422d3974f8f1a033a96a91aed3d3e60f09477de))

## [0.4.2](https://github.com/LaurensDeV/KSArmory/compare/v0.4.1...v0.4.2) (2026-08-03)

### Fixes

* **rounds:** fuse on proximity, not on the seeker holding the target ([5b9e68e](https://github.com/LaurensDeV/KSArmory/commit/5b9e68e3a1ddc5c9f1256846318564885377bf7f))

### Documentation

* do not commit a behaviour fix until it is verified in game ([28b2d14](https://github.com/LaurensDeV/KSArmory/commit/28b2d1474d5efe6bdc2fa05726aab7cc85cc3c0f))

## [0.4.1](https://github.com/LaurensDeV/KSArmory/compare/v0.4.0...v0.4.1) (2026-08-03)

### Fixes

* **sim:** step on the delta KSA applied, not one measured around it ([80023e4](https://github.com/LaurensDeV/KSArmory/commit/80023e492418a38b128eb10bf6981b4664386a19))

## [0.4.0](https://github.com/LaurensDeV/KSArmory/compare/v0.3.2...v0.4.0) (2026-08-03)

### Features

* **rounds:** add a round-body toggle and placement tracing ([0735022](https://github.com/LaurensDeV/KSArmory/commit/0735022262b1f6edd1f1ca4b2879797d7ab49510))

### Fixes

* **build:** silence the two nullable warnings in TestTarget ([a2bcb04](https://github.com/LaurensDeV/KSArmory/commit/a2bcb041214327742ec95425152d1fcc85c122d1))

## [0.3.2](https://github.com/LaurensDeV/KSArmory/compare/v0.3.1...v0.3.2) (2026-08-03)

### Fixes

* **rounds:** place round bodies every frame, not every simulation step ([609d783](https://github.com/LaurensDeV/KSArmory/commit/609d783846ba6763740f7666a58ffe4e6da1c554))

## [0.3.1](https://github.com/LaurensDeV/KSArmory/compare/v0.3.0...v0.3.1) (2026-08-03)

### Fixes

* **sim:** step on simulation time, not wall-clock player time ([21f07e7](https://github.com/LaurensDeV/KSArmory/commit/21f07e71af994846b8fd603e12bed3e88555ed92))

### Internal

* **radar:** move threat ranking into Sim/ so it can be tested ([5876c61](https://github.com/LaurensDeV/KSArmory/commit/5876c6119af7ad61400f52efda76244b79becb18))

## [0.3.0](https://github.com/LaurensDeV/KSArmory/compare/v0.2.0...v0.3.0) (2026-08-03)

### Features

* **tools:** mirror and decompile the whole KSA SDK ([4bf7ed7](https://github.com/LaurensDeV/KSArmory/commit/4bf7ed7811fa2e05a6dfabe30342149ce11a63fa))
* **tools:** narrow a KSA update to the changes that touch this mod ([cd3fe6b](https://github.com/LaurensDeV/KSArmory/commit/cd3fe6b21d23783d6cddf8c887fd24aabce14c83))
* **tools:** record the KSA API surface this mod binds to ([e5f9c87](https://github.com/LaurensDeV/KSArmory/commit/e5f9c87d653d72753d85ecabc9b9d9a836ce0117))

### Documentation

* add an upgrade-ksa skill and record the new workflow ([1142ca4](https://github.com/LaurensDeV/KSArmory/commit/1142ca49cbf0363aa539a73adc15b3af1a53df75))

## [0.2.0](https://github.com/LaurensDeV/KSArmory/compare/v0.1.1...v0.2.0) (2026-08-03)

### Features

* **model:** compare two mesh atlases by geometry, not by bytes ([212418e](https://github.com/LaurensDeV/KSArmory/commit/212418e8f54d5bfe1c92382fd28394222c18fb01))
* **tools:** notice a KSA update on the first build after it ([04a1746](https://github.com/LaurensDeV/KSArmory/commit/04a1746f86090664a25507ce3098bc5aec045635))
* **tools:** watch RocketWerkz's version endpoint for KSA updates ([89ba25f](https://github.com/LaurensDeV/KSArmory/commit/89ba25f9bb2fbbdf9f3f92e1585cfc9c427bb45c))

### Documentation

* cross-reference the KSA update procedure from the commands list ([f0aec7c](https://github.com/LaurensDeV/KSArmory/commit/f0aec7c188a5ebe3088e389ee755e9689044bc87))

## [0.1.1](https://github.com/LaurensDeV/KSArmory/compare/v0.1.0...v0.1.1) (2026-08-03)

### Build and packaging

* **release:** add publish-release.sh for machines with KSA ([6af1bed](https://github.com/LaurensDeV/KSArmory/commit/6af1bed582c9f30048b215100cd8f3f52d4d1f1d))
* resolve the KSA assembly folder in tiers ([a7f72e7](https://github.com/LaurensDeV/KSArmory/commit/a7f72e7a08313a597f30cf8268387ffb538593e5))
