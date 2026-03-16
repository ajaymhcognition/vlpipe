# VLab Pipe — Unity Addressables Build & Upload Plugin

**VLab Pipe** is a Unity Editor plugin that builds WebGL Addressables and uploads them directly to AWS S3 — with a single click, from inside Unity.

Install it once via the Unity Package Manager. No command line. No manual setup. No credentials in code.

---

## Install

In Unity, open **Window → Package Manager → + → Add package from git URL** and enter:

```
https://github.com/ajaymhcognition/vlpipe.git
```

Unity will import the package automatically. All menu items appear under **Tools → Virtual Lab** once import is complete.

---

## First-Time Setup

Run these steps once per project, in order.

### Step 1 — Enter AWS Credentials *(Senior Developer only)*

```
Tools → Virtual Lab → AWS Settings
```

Enter the **Access Key ID** and **Secret Access Key**, then click **Save Credentials**.

Credentials are stored on this machine only — never in the project folder, never in Git. Once saved, no one can see the values again. The window shows only a green confirmation to anyone who opens it after you.

### Step 2 — Configure the Project

```
Tools → Virtual Lab → Project Setup
```

A step-by-step wizard opens. Complete all 7 steps in order:

| Step | What it does |
|------|--------------|
| 1 | Installs the Addressables package |
| 2 | Creates the module folder — fill in Board, Grade, Subject, Unit / Chapter, and Topic |
| 3 | Creates Addressables Settings |
| 4 | Configures build and load profiles |
| 5 | Configures the Default Local Group for remote delivery |
| 6 | Adds scenes to Addressables groups |
| 7 | Saves and finishes |

#### Step 2 — Unit / Chapter Field

The Unit / Chapter field accepts any string and stores it **exactly as typed** — no automatic shortening or normalisation is applied.

| What you type | Folder created | LMS must send |
|---------------|----------------|---------------|
| `Unit2` | `…/Unit2/…` | `&unit=Unit2` |
| `Chapter5` | `…/Chapter5/…` | `&unit=Chapter5` |
| `Semester1` | `…/Semester1/…` | `&unit=Semester1` |
| `MyCustomValue` | `…/MyCustomValue/…` | `&unit=MyCustomValue` |

The same rule applies to Grade — the dropdown stores the full enum name:

| Dropdown selection | Folder created | LMS must send |
|--------------------|----------------|---------------|
| Grade12 | `…/Grade12/…` | `&grade=Grade12` |
| Grade10 | `…/Grade10/…` | `&grade=Grade10` |

> **The LMS URL must send the exact same strings that were entered during Project Setup.** The Virtual Lab Dashboard passes them through to S3 without any modification.

---

## Daily Use — Build & Upload

```
Tools → Virtual Lab → Pipeline → Build And Upload To S3
```

The pipeline runs in this order:

```
1. Clean     — deletes stale ServerData, clears Addressables cache
2. Platform  — confirms or switches to WebGL
3. Build     — compiles Addressables, generates remote catalog (JSON)
4. Upload    — pushes all files to S3
```

A progress bar shows every step. A dialog confirms when the upload is complete.

---

## Tools at a Glance

| Menu item | Who uses it | When |
|-----------|-------------|------|
| `Tools → Virtual Lab → AWS Settings` | Senior developer | Once per machine |
| `Tools → Virtual Lab → Project Setup` | Any developer | Once per new project |
| `Tools → Virtual Lab → Pipeline → Build And Upload To S3` | Any developer | Every publish |
| `Tools → Virtual Lab → Pipeline → Clean Build Cache` | Any developer | Manual clean if needed |

---

## AWS Credentials — How It Works

Credentials are **never stored in any file inside the project**. They are saved in Unity's `EditorPrefs` (Windows Registry or macOS plist).

| Who opens AWS Settings | What they see |
|------------------------|---------------|
| Senior (first time) | Entry form — paste keys and save |
| Junior (any time after) | Green status only — no fields, no values |
| Senior (needs to update) | Clicks **Update Credentials** → confirms → enters new keys |

If credentials are missing when a build is triggered, Unity shows a dialog and opens the AWS Settings window automatically.

---

## S3 Upload Path

Files are uploaded under this structure:

```
s3://<bucket>/Modules/<Board>/<Grade>/<Subject>/<Unit>/<Topic>/<BuildTarget>/
```

Example:

```
s3://your-bucket/Modules/CBSE/Grade12/Physics/Unit2/Optics/WebGL/
  catalog_mhcockpit.json
  optics_bundle.bundle
```

### Path Segment Rules

Every segment is stored and uploaded **exactly as entered** — no shortening or reformatting.

| Segment | Source | Example |
|---------|--------|---------|
| Board | Board dropdown | `CBSE` |
| Grade | Grade dropdown (full enum name) | `Grade12` |
| Subject | Subject dropdown | `Physics` |
| Unit / Chapter | Free-text — stored as typed | `Unit2`, `Chapter5`, `Semester1` |
| Topic | Auto-filled from project name (PascalCase) | `Optics` |
| BuildTarget | Addressables build setting | `WebGL` |

---

## Requirements

- Unity 2022.3 LTS or newer (Unity 6 supported)
- WebGL Build Support installed via Unity Hub
- Unity Addressables package (the setup wizard installs this for you)
- An AWS S3 bucket with write access

---

## .gitignore

Add this to your `.gitignore` to keep build output out of the repository:

```gitignore
# Addressables remote build output — rebuilt on every run
ServerData/
```