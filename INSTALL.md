# Setting up Rafter Cat's Looking Glass

Same steps as the Chore List.

## 1. Make the GitHub repo
1. On GitHub (as The-Kinship), make a new **public** repo called `looking-glass`. Leave it empty.
2. Click **uploading an existing file**, drag in everything from this zip (including the `.github` folder), commit.
   No `.github` folder after uploading? **Add file > Create new file**, name it `.github/workflows/build.yml`,
   paste in that file's contents, commit.
3. **Settings > Actions > General > Workflow permissions**: **Read and write permissions**, Save.
4. **Actions** tab: wait for "Build plugin" to go green (or press **Run workflow**).

## 2. Install it in game
1. `/xlsettings` > **Experimental** > **Custom Plugin Repositories**.
2. Add `https://raw.githubusercontent.com/The-Kinship/looking-glass/main/repo.json`, press +, Save.
3. `/xlplugins`, search **Looking Glass**, Install.
4. Type `/lglass`.

## 3. Try it
- Your mods should be listed in the same folder order as Penumbra, with a count at the top.
- Type "top" or "laces" in Search. Try two words.
- Pick a folder in the folder drop-down.
- Click a mod, then press the down arrow a few times.
- Click **Copy name**, open Penumbra and paste it into its search box.
- If it says it couldn't find your mods, open **Settings** at the bottom and paste your Penumbra mod folder
  (Penumbra > Settings > Root Directory), then **Save and reload**.
