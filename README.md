# VLab Pipe — Unity Addressables Build & Upload Plugin

VLab Pipe is a Unity Editor plugin that builds WebGL Addressables and uploads them directly to AWS S3 — single click, from inside Unity.

Install once via Unity Package Manager. No command line. No manual S3 management. No credentials in code.

---

## Install

**Window → Package Manager → + → Add package from git URL:**

```
https://github.com/ajaymhcognition/vlpipe.git
```

All menu items appear under **Tools → Virtual Lab** once import completes.

---

## First-Time Setup

Run these steps once per project, in order.

### Step 1 — Enter AWS Credentials *(Senior Developer only)*

```
Tools → Virtual Lab → AWS Settings
```

Enter **Access Key ID** and **Secret Access Key**, then click **Save Credentials**.

Credentials are stored in Unity `EditorPrefs` (Windows Registry / macOS plist) on this machine only — never in the project folder, never in Git. Once saved, the window shows only a green confirmation. No one can view the values after saving.

### Step 2 — Configure the Project

```
Tools → Virtual Lab → Project Setup
```

A 7-step wizard opens. Complete all steps in order:

| Step | What It Does |
|------|--------------|
| 1 | Installs the Addressables package |
| 2 | Creates the module folder structure — Board, Grade, Subject, Unit Type + Number, and Topic |
| 3 | Creates Addressables Settings asset |
| 4 | Configures build and load profiles for remote delivery |
| 5 | Configures the Default Local Group with remote paths, LZ4, CRC, Append Hash |
| 6 | Adds Practice and Evaluation scenes to Addressables |
| 7 | Saves all assets and finalises |

#### Step 2 — Unit Type and Number

The wizard provides a **Unit Type** dropdown (`Unit` or `Chapter`) and a **Number** field. These combine to produce the S3 folder name:

| Unit Type | Number | Folder Created | LMS Must Send |
|-----------|--------|----------------|---------------|
| `Unit` | `2` | `…/Unit2/…` | `&unit=Unit2` |
| `Chapter` | `5` | `…/Chapter5/…` | `&unit=Chapter5` |
| `Unit` | `1` | `…/Unit1/…` | `&unit=Unit1` |
| `Chapter` | `3` | `…/Chapter3/…` | `&unit=Chapter3` |

The combined value is written into `module_config.json` and used as the S3 upload prefix. The LMS URL must send the exact same string in the `unit` parameter.

The same rule applies to Grade — the dropdown stores the full enum name (`Grade12`, not `12`).

---

## Daily Use — Build & Upload

```
Tools → Virtual Lab → Pipeline → Build And Upload To S3
```

The pipeline runs four steps automatically:

```
1. Clean     — deletes stale ServerData folder, clears Addressables content cache
2. Platform  — confirms or switches to WebGL
3. Build     — compiles Addressables, generates remote JSON catalog
4. Upload    — pushes all output files to S3
```

A progress bar shows every step. A dialog confirms when the upload completes.

---

## Tools at a Glance

| Menu Item | Who | When |
|-----------|-----|------|
| `Tools → Virtual Lab → AWS Settings` | Senior developer | Once per machine |
| `Tools → Virtual Lab → Project Setup` | Any developer | Once per new project |
| `Tools → Virtual Lab → Pipeline → Build And Upload To S3` | Any developer | Every publish |
| `Tools → Virtual Lab → Pipeline → Clean Build Cache` | Any developer | Manual clean if needed |

---

## AWS Credentials — Security Model

Credentials never appear in any project file. Storage locations:

| Source | Used By |
|--------|---------|
| `EditorPrefs` (local machine) | Interactive Unity Editor — set via AWS Settings window |
| Environment variables (`AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, `AWS_REGION`) | GitHub Actions CI/CD |

If credentials are missing when Build And Upload runs, Unity shows a dialog and opens the AWS Settings window automatically.

| Who Opens AWS Settings | What They See |
|------------------------|---------------|
| Senior (first time) | Entry form — paste keys and save |
| Junior (any time after) | Green status only — no fields, no values visible |
| Senior (needs to update) | Clicks **Update Credentials** → confirms → enters new keys |

---

## S3 Upload Path

Files are uploaded to:

```
s3://<bucket>/Modules/<Board>/<Grade>/<Subject>/<Unit>/<Topic>/<BuildTarget>/
```

Examples:

```
s3://mhc-embibe-test/Modules/CBSE/Grade11/Physics/Unit2/DeterminingMassOfABodyUsingMeterScale/WebGL/
  catalog_mhcockpit.json
  *.bundle
  *.hash

s3://mhc-embibe-test/Modules/CBSE/Grade12/Physics/Chapter5/Optics/WebGL/
  catalog_mhcockpit.json
  *.bundle
  *.hash
```

| Segment | Source | Examples |
|---------|--------|----------|
| Board | Board dropdown | `CBSE`, `ICSE`, `StateBoard` |
| Grade | Grade dropdown (full enum name) | `Grade12`, `Grade11` |
| Subject | Subject dropdown | `Physics`, `Chemistry` |
| Unit | UnitType dropdown + Number field | `Unit2`, `Chapter5` |
| Topic | Auto-filled from project name (PascalCase) | `Optics`, `DeterminingMassOfABodyUsingMeterScale` |
| BuildTarget | Addressables build setting | `WebGL` |

---

## Requirements

- Unity 2022.3 LTS or newer (Unity 6 supported)
- WebGL Build Support installed via Unity Hub
- Unity Addressables package (the wizard installs this at Step 1)
- AWS S3 bucket with write access

---

## .gitignore

```gitignore
ServerData/
```