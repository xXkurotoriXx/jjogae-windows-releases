# 검증 안내

Windows 릴리스는 같은 소스를 두 Windows 작업에서 독립적으로 빌드하고 검사합니다. [Windows build](https://github.com/xXkurotoriXx/jjogae-windows-releases/actions/workflows/windows-build.yml)에서 결과를 확인할 수 있습니다. 실행 파일·SHA-256 체크섬은 [최신 릴리스](https://github.com/xXkurotoriXx/jjogae-windows-releases/releases)에 포함됩니다. 검사 보고서와 화면 캡처는 CI 아티팩트에서 3일 동안 받을 수 있습니다.

## 검사 범위

- 치즈·방송·카페 데이터 파싱, 중복 방지와 백업 복원
- 방송 시각·분할 영상·날짜 경계와 시간 합계
- 공개 이용 불가 영상 확인과 관련 기록 정리
- 카페 공지 삭제 응답, 저장·읽음·알림 식별값 정리와 재조회 시 복원 방지
- 조회 후 삭제된 공지의 복원 차단, 취소·권한 오류·통신 실패와 재시도
- YouTube 공개 정보와 개인 구독·멤버십 연결
- 계정 프로필과 로그인 브라우저 재진입
- Windows 알림과 시작프로그램 등록·해제
- 업데이트 검증·복원과 완료된 임시 파일 정리
- 진행 중인 업데이트·다른 설치·예상 밖 파일·링크 대상의 보존
- 기록·이미지·웹 데이터·업데이트의 용량 구분과 저장·내보내기 호환성
- 라이트·다크 모드와 430/719/720/1000/1500px 화면 배치
- 컬러 이모지와 접근성 이름

자동 화면 검사는 별도 임시 프로필과 모의 데이터를 사용합니다. 실제 로그인·외부 서비스 응답·알림 배너·여러 모니터와 DPI·절전 복귀는 실제 PC에서도 확인해야 합니다.

## 개발 참고

용량 계산과 업데이트 정리는 화면 스레드 밖에서 처리합니다. 기록은 같은 JSON 형식으로 간결하게 저장하고 내보내기는 읽기 쉬운 형식을 유지합니다. 로그인 프로필과 사용자 기록은 업데이트 임시 파일 정리 대상에 포함하지 않습니다.

- [Microsoft WPF 스레드 모델](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/threading-model)
- [Microsoft WebView2 성능 권장 사항](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/performance)
- [Microsoft WebView2 사용자 데이터 관리](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/user-data-folder)
