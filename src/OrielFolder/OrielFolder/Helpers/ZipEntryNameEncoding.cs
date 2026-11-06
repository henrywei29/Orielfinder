using System.Buffers;
using System.Text;
using System.Text.Unicode;

namespace OrielFolder.Helpers;

/// <summary>
/// ZIP 읽기 전용 파일명 인코딩. 자동 모드는 유효한 UTF-8을 우선하고, 그 외에는 OS ANSI를 쓴다.
/// 명시적 선택은 EFS 없는 이름을 해당 코드페이지로 읽는다. EFS 항목은 ZipEntryNames가 UTF-8로 읽는다.
/// EFS 없는 CP949/936 등의 바이트는 겹칠 수 있어 언어를 확정적으로 자동 판별할 수 없다.
/// 다른 지역에서 만든 ZIP은 사용자가 원본 코드페이지를 선택해야 한다.
/// </summary>
internal sealed class ZipEntryNameEncoding : Encoding
{
    /// <summary>
    /// 공용 인스턴스. ZIP <b>읽기</b> 경로에서만 쓴다.
    /// 쓰기(압축 생성)는 .NET 기본 동작(UTF-8 + EFS 비트)이 상호운용에 맞으므로 건드리지 않는다.
    /// </summary>
    internal static readonly ZipEntryNameEncoding Instance = new();

    internal static Encoding ForCodePage(int legacyCodePage)
    {
        if (legacyCodePage == 0) return Instance;
        if (legacyCodePage is not (949 or 936 or 950 or 932 or 1252))
            throw new ArgumentOutOfRangeException(nameof(legacyCodePage));

        RegisterProvider(CodePagesEncodingProvider.Instance);
        return GetEncoding(legacyCodePage);
    }

    /// <summary>잘못된 바이트를 U+FFFD로 치환하는 UTF-8. 판별을 통과한 이름에만 쓰인다.</summary>
    private static readonly UTF8Encoding Utf8Lax = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    /// <summary>자동 모드에서 UTF-8이 아닌 이름에 쓸 OS ANSI 코드페이지. 사용할 수 없으면 null.</summary>
    private readonly Encoding? _ansi;

    private ZipEntryNameEncoding() => _ansi = ResolveAnsi();

    private static Encoding? ResolveAnsi()
    {
        try
        {
            // .NET Core는 기본적으로 레거시 코드페이지를 싣지 않는다. 등록은 멱등이다.
            RegisterProvider(CodePagesEncodingProvider.Instance);

            // GetEncoding(0)은 시스템 ANSI 코드페이지다. CurrentCulture는 표시 형식 설정이므로
            // 시스템 로캘과 다를 수 있다.
            var encoding = GetEncoding(0);

            // ANSI가 UTF-8인 환경("Beta: Use Unicode UTF-8 for worldwide language support")에서는
            // 폴백이 의미가 없다 — UTF-8 경로가 이미 처리한다. 이 경우 기존 동작을 유지한다.
            if (encoding.CodePage != 65001)
                return encoding;
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[Zip] ANSI codepage unavailable, falling back to UTF-8 only: {ex.GetType().Name}");
        }
        return null;
    }

    /// <summary>이름 바이트가 유효한 UTF-8이면 UTF-8을, 아니면 ANSI 코드페이지를 고른다.</summary>
    private Encoding Pick(byte[] bytes, int index, int count)
    {
        if (_ansi is null || count == 0)
            return Utf8Lax;

        // UTF-8 -> UTF-16 변환에서 문자 수는 바이트 수를 넘지 않으므로 count 크기면 항상 충분하다.
        char[]? rented = null;
        try
        {
            Span<char> scratch = count <= 256
                ? stackalloc char[256]
                : (rented = ArrayPool<char>.Shared.Rent(count));

            var status = Utf8.ToUtf16(
                bytes.AsSpan(index, count), scratch, out _, out _,
                replaceInvalidSequences: false, isFinalBlock: true);

            return status == OperationStatus.Done ? Utf8Lax : _ansi;
        }
        finally
        {
            if (rented is not null) ArrayPool<char>.Shared.Return(rented);
        }
    }

    public override int GetCharCount(byte[] bytes, int index, int count)
        => Pick(bytes, index, count).GetCharCount(bytes, index, count);

    public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
        => Pick(bytes, byteIndex, byteCount).GetChars(bytes, byteIndex, byteCount, chars, charIndex);

    public override int GetMaxCharCount(int byteCount)
        => Math.Max(Utf8Lax.GetMaxCharCount(byteCount), _ansi?.GetMaxCharCount(byteCount) ?? 0);

    // 아래 인코딩(문자 -> 바이트) 경로는 읽기 전용 사용이라 호출되지 않지만,
    // Encoding이 추상 멤버로 요구하므로 UTF-8로 위임한다.
    public override int GetByteCount(char[] chars, int index, int count)
        => Utf8Lax.GetByteCount(chars, index, count);

    public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
        => Utf8Lax.GetBytes(chars, charIndex, charCount, bytes, byteIndex);

    public override int GetMaxByteCount(int charCount)
        => Utf8Lax.GetMaxByteCount(charCount);
}
