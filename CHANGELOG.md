## [0.4.2](https://github.com/LaurensDeV/KSA-AirDefence/compare/v0.4.1...v0.4.2) (2026-08-03)

### Fixes

* **rounds:** fuse on proximity, not on the seeker holding the target ([5b9e68e](https://github.com/LaurensDeV/KSA-AirDefence/commit/5b9e68e3a1ddc5c9f1256846318564885377bf7f))

### Documentation

* do not commit a behaviour fix until it is verified in game ([28b2d14](https://github.com/LaurensDeV/KSA-AirDefence/commit/28b2d1474d5efe6bdc2fa05726aab7cc85cc3c0f))

## [0.4.1](https://github.com/LaurensDeV/KSA-AirDefence/compare/v0.4.0...v0.4.1) (2026-08-03)

### Fixes

* **sim:** step on the delta KSA applied, not one measured around it ([80023e4](https://github.com/LaurensDeV/KSA-AirDefence/commit/80023e492418a38b128eb10bf6981b4664386a19))

## [0.4.0](https://github.com/LaurensDeV/KSA-AirDefence/compare/v0.3.2...v0.4.0) (2026-08-03)

### Features

* **rounds:** add a round-body toggle and placement tracing ([0735022](https://github.com/LaurensDeV/KSA-AirDefence/commit/0735022262b1f6edd1f1ca4b2879797d7ab49510))

### Fixes

* **build:** silence the two nullable warnings in TestTarget ([a2bcb04](https://github.com/LaurensDeV/KSA-AirDefence/commit/a2bcb041214327742ec95425152d1fcc85c122d1))

## [0.3.2](https://github.com/LaurensDeV/KSA-AirDefence/compare/v0.3.1...v0.3.2) (2026-08-03)

### Fixes

* **rounds:** place round bodies every frame, not every simulation step ([609d783](https://github.com/LaurensDeV/KSA-AirDefence/commit/609d783846ba6763740f7666a58ffe4e6da1c554))

## [0.3.1](https://github.com/LaurensDeV/KSA-AirDefence/compare/v0.3.0...v0.3.1) (2026-08-03)

### Fixes

* **sim:** step on simulation time, not wall-clock player time ([21f07e7](https://github.com/LaurensDeV/KSA-AirDefence/commit/21f07e71af994846b8fd603e12bed3e88555ed92))

### Internal

* **radar:** move threat ranking into Sim/ so it can be tested ([5876c61](https://github.com/LaurensDeV/KSA-AirDefence/commit/5876c6119af7ad61400f52efda76244b79becb18))

## [0.3.0](https://github.com/LaurensDeV/KSA-AirDefence/compare/v0.2.0...v0.3.0) (2026-08-03)

### Features

* **tools:** mirror and decompile the whole KSA SDK ([4bf7ed7](https://github.com/LaurensDeV/KSA-AirDefence/commit/4bf7ed7811fa2e05a6dfabe30342149ce11a63fa))
* **tools:** narrow a KSA update to the changes that touch this mod ([cd3fe6b](https://github.com/LaurensDeV/KSA-AirDefence/commit/cd3fe6b21d23783d6cddf8c887fd24aabce14c83))
* **tools:** record the KSA API surface this mod binds to ([e5f9c87](https://github.com/LaurensDeV/KSA-AirDefence/commit/e5f9c87d653d72753d85ecabc9b9d9a836ce0117))

### Documentation

* add an upgrade-ksa skill and record the new workflow ([1142ca4](https://github.com/LaurensDeV/KSA-AirDefence/commit/1142ca49cbf0363aa539a73adc15b3af1a53df75))

## [0.2.0](https://github.com/LaurensDeV/KSA-AirDefence/compare/v0.1.1...v0.2.0) (2026-08-03)

### Features

* **model:** compare two mesh atlases by geometry, not by bytes ([212418e](https://github.com/LaurensDeV/KSA-AirDefence/commit/212418e8f54d5bfe1c92382fd28394222c18fb01))
* **tools:** notice a KSA update on the first build after it ([04a1746](https://github.com/LaurensDeV/KSA-AirDefence/commit/04a1746f86090664a25507ce3098bc5aec045635))
* **tools:** watch RocketWerkz's version endpoint for KSA updates ([89ba25f](https://github.com/LaurensDeV/KSA-AirDefence/commit/89ba25f9bb2fbbdf9f3f92e1585cfc9c427bb45c))

### Documentation

* cross-reference the KSA update procedure from the commands list ([f0aec7c](https://github.com/LaurensDeV/KSA-AirDefence/commit/f0aec7c188a5ebe3088e389ee755e9689044bc87))

## [0.1.1](https://github.com/LaurensDeV/KSA-AirDefence/compare/v0.1.0...v0.1.1) (2026-08-03)

### Build and packaging

* **release:** add publish-release.sh for machines with KSA ([6af1bed](https://github.com/LaurensDeV/KSA-AirDefence/commit/6af1bed582c9f30048b215100cd8f3f52d4d1f1d))
* resolve the KSA assembly folder in tiers ([a7f72e7](https://github.com/LaurensDeV/KSA-AirDefence/commit/a7f72e7a08313a597f30cf8268387ffb538593e5))
