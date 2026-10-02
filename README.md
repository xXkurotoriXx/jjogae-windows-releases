# 쪼개 상황실 · Windows

아홀로 루파의 치지직·YouTube 채널과 네이버 카페 정보를 모아 보는 Windows 앱입니다.

## 설치

Windows 10/11 x64에서 사용합니다. .NET을 별도로 설치할 필요가 없습니다.

1. [최신 릴리스](https://github.com/xXkurotoriXx/jjogae-windows-releases/releases/latest)에서 **JjogaeStatus.exe**를 받습니다.
2. 사용자가 쓸 수 있는 폴더에 두고 실행합니다.
3. 필요한 서비스 기능에 네이버·치지직·YouTube로 로그인합니다. 로그인 화면에는 Microsoft Edge WebView2 Runtime이 필요합니다.

## 기존 설치에서 한 번 전환하기

**0.4.14 이하 버전은 새 배포 주소를 모르므로 0.4.15를 한 번 직접 설치해야 합니다.**

1. 새 공개 릴리스에서 JjogaeStatus.exe를 받습니다. GitHub 로그인은 필요하지 않습니다.
2. 실행 중인 기존 앱을 메뉴의 **프로그램 종료**로 끝냅니다. X 버튼은 최소화하므로 종료 메뉴를 사용하세요.
3. 기존 JjogaeStatus.exe를 새 파일로 교체하고 실행합니다. 실행 파일 경로를 유지하면 기존 바로가기와 시작 프로그램 등록을 그대로 사용할 수 있습니다.
4. %LOCALAPPDATA%\JjogaeWindows 폴더를 삭제하지 마세요. 기록·설정·서비스 로그인 프로필은 이 폴더에서 유지됩니다.

## 앱 내 업데이트

0.4.15부터 공개 릴리스에서 GitHub 로그인 없이 새 버전을 확인하고 내려받습니다. Git이나 Git Credential Manager 설치도 필요 없습니다. 서비스 로그인과 앱 업데이트는 별개입니다.

설정의 **업데이트 → 업데이트 확인**에서 수동 확인할 수 있습니다. 자동 확인을 켜면 앱 실행 중 주 1회 확인합니다. 새 버전을 내려받아 크기·SHA-256·파일 버전을 확인하고 실행 파일을 교체한 뒤 재실행합니다. 실패하면 기존 파일을 보존하거나 복원하며 다시 확인할 수 있습니다.

설치 파일, SHA256SUMS.txt, 업데이트 정보만 공개 배포합니다. 앱 사용자 기록·로그인 정보는 배포에 포함하지 않습니다.

[개인정보처리방침](PRIVACY.md) · [오픈소스 고지](THIRD_PARTY_NOTICES.md) · [오류 제보](https://github.com/xXkurotoriXx/jjogae-windows-releases/issues)
