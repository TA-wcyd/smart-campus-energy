@echo off
echo [1/3] Staging all changes...
git -C "e:\bup\smart-campus-energy" add -A

echo [2/3] Committing...
git -C "e:\bup\smart-campus-energy" commit -m "feat(ratul): Member 3 deliverables complete and conflict-free"

echo [3/3] Pushing to ratul branch...
git -C "e:\bup\smart-campus-energy" push origin ratul

echo.
echo Opening GitHub Pull Request page...
start https://github.com/TA-wcyd/smart-campus-energy/compare/main...ratul?expand=1

echo.
echo Done! Click "Create pull request" on GitHub to finish merging.
pause
