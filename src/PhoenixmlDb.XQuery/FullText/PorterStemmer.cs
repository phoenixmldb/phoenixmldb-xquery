namespace PhoenixmlDb.XQuery.FullText;

/// <summary>
/// The Porter stemming algorithm (M. F. Porter, "An algorithm for suffix stripping", Program
/// 14(3), 1980), with the two changes of Porter's own reference implementation: step 2 maps
/// "bli" to "ble" (in place of "abli" to "able") and "logi" to "log". Words of one or two
/// characters are not changed. Only lowercase a, e, i, o, u (and y after a consonant) are vowels.
/// </summary>
internal sealed class PorterStemmer
{
    private char[] _b = new char[64];
    private int _k;   // index of the last character of the current word
    private int _j;   // index of the last character before the suffix that Ends matched

    /// <summary>The stem of <paramref name="word"/>.</summary>
    public string Stem(string word)
    {
        if (word.Length <= 2)
            return word;
        if (_b.Length < word.Length)
            _b = new char[word.Length * 2];
        word.CopyTo(0, _b, 0, word.Length);
        _k = word.Length - 1;
        Step1ab();
        Step1c();
        Step2();
        Step3();
        Step4();
        Step5();
        return _k + 1 == word.Length && _b.AsSpan(0, _k + 1).SequenceEqual(word)
            ? word : new string(_b, 0, _k + 1);
    }

    private bool IsConsonant(int i) => _b[i] switch
    {
        'a' or 'e' or 'i' or 'o' or 'u' => false,
        'y' => i == 0 || !IsConsonant(i - 1),
        _ => true,
    };

    /// <summary>The measure m of the stem _b[0.._j]: the number of vowel-consonant sequences.</summary>
    private int Measure()
    {
        var n = 0;
        var i = 0;
        while (true)
        {
            if (i > _j) return n;
            if (!IsConsonant(i)) break;
            i++;
        }
        i++;
        while (true)
        {
            while (true)
            {
                if (i > _j) return n;
                if (IsConsonant(i)) break;
                i++;
            }
            i++;
            n++;
            while (true)
            {
                if (i > _j) return n;
                if (!IsConsonant(i)) break;
                i++;
            }
            i++;
        }
    }

    private bool StemHasVowel()
    {
        for (var i = 0; i <= _j; i++)
        {
            if (!IsConsonant(i))
                return true;
        }
        return false;
    }

    private bool DoubleConsonant(int i) => i >= 1 && _b[i] == _b[i - 1] && IsConsonant(i);

    /// <summary>Consonant-vowel-consonant ending at i, the last consonant not w, x or y.</summary>
    private bool Cvc(int i)
        => i >= 2 && IsConsonant(i) && !IsConsonant(i - 1) && IsConsonant(i - 2)
           && _b[i] is not ('w' or 'x' or 'y');

    private bool Ends(string suffix)
    {
        var length = suffix.Length;
        if (length > _k + 1 || _b[_k] != suffix[length - 1])
            return false;
        if (!_b.AsSpan(_k - length + 1, length).SequenceEqual(suffix))
            return false;
        _j = _k - length;
        return true;
    }

    private void SetTo(string replacement)
    {
        replacement.CopyTo(0, _b, _j + 1, replacement.Length);
        _k = _j + replacement.Length;
    }

    private void ReplaceIfMeasured(string replacement)
    {
        if (Measure() > 0)
            SetTo(replacement);
    }

    // Step 1a: plurals; step 1b: -eed, -ed, -ing.
    private void Step1ab()
    {
        if (_b[_k] == 's')
        {
            if (Ends("sses")) _k -= 2;
            else if (Ends("ies")) SetTo("i");
            else if (_b[_k - 1] != 's') _k--;
        }
        if (Ends("eed"))
        {
            if (Measure() > 0) _k--;
        }
        else if ((Ends("ed") || Ends("ing")) && StemHasVowel())
        {
            _k = _j;
            if (Ends("at")) SetTo("ate");
            else if (Ends("bl")) SetTo("ble");
            else if (Ends("iz")) SetTo("ize");
            else if (DoubleConsonant(_k))
            {
                var c = _b[_k--];
                if (c is 'l' or 's' or 'z') _k++;
            }
            else if (Measure() == 1 && Cvc(_k)) SetTo("e");
        }
    }

    // Step 1c: y to i when the stem has a vowel.
    private void Step1c()
    {
        if (Ends("y") && StemHasVowel())
            _b[_k] = 'i';
    }

    // Step 2: double suffixes to single ones, when m > 0.
    private void Step2()
    {
        if (_k == 0) return;
        switch (_b[_k - 1])
        {
            case 'a':
                if (Ends("ational")) { ReplaceIfMeasured("ate"); break; }
                if (Ends("tional")) { ReplaceIfMeasured("tion"); break; }
                break;
            case 'c':
                if (Ends("enci")) { ReplaceIfMeasured("ence"); break; }
                if (Ends("anci")) { ReplaceIfMeasured("ance"); break; }
                break;
            case 'e':
                if (Ends("izer")) { ReplaceIfMeasured("ize"); break; }
                break;
            case 'l':
                if (Ends("bli")) { ReplaceIfMeasured("ble"); break; }
                if (Ends("alli")) { ReplaceIfMeasured("al"); break; }
                if (Ends("entli")) { ReplaceIfMeasured("ent"); break; }
                if (Ends("eli")) { ReplaceIfMeasured("e"); break; }
                if (Ends("ousli")) { ReplaceIfMeasured("ous"); break; }
                break;
            case 'o':
                if (Ends("ization")) { ReplaceIfMeasured("ize"); break; }
                if (Ends("ation")) { ReplaceIfMeasured("ate"); break; }
                if (Ends("ator")) { ReplaceIfMeasured("ate"); break; }
                break;
            case 's':
                if (Ends("alism")) { ReplaceIfMeasured("al"); break; }
                if (Ends("iveness")) { ReplaceIfMeasured("ive"); break; }
                if (Ends("fulness")) { ReplaceIfMeasured("ful"); break; }
                if (Ends("ousness")) { ReplaceIfMeasured("ous"); break; }
                break;
            case 't':
                if (Ends("aliti")) { ReplaceIfMeasured("al"); break; }
                if (Ends("iviti")) { ReplaceIfMeasured("ive"); break; }
                if (Ends("biliti")) { ReplaceIfMeasured("ble"); break; }
                break;
            case 'g':
                if (Ends("logi")) { ReplaceIfMeasured("log"); break; }
                break;
        }
    }

    // Step 3: -ic-, -full, -ness and similar, when m > 0.
    private void Step3()
    {
        switch (_b[_k])
        {
            case 'e':
                if (Ends("icate")) { ReplaceIfMeasured("ic"); break; }
                if (Ends("ative")) { ReplaceIfMeasured(""); break; }
                if (Ends("alize")) { ReplaceIfMeasured("al"); break; }
                break;
            case 'i':
                if (Ends("iciti")) { ReplaceIfMeasured("ic"); break; }
                break;
            case 'l':
                if (Ends("ical")) { ReplaceIfMeasured("ic"); break; }
                if (Ends("ful")) { ReplaceIfMeasured(""); break; }
                break;
            case 's':
                if (Ends("ness")) { ReplaceIfMeasured(""); break; }
                break;
        }
    }

    // Step 4: remove -ant, -ence and similar, when m > 1.
    private void Step4()
    {
        if (_k == 0) return;
        switch (_b[_k - 1])
        {
            case 'a':
                if (Ends("al")) break;
                return;
            case 'c':
                if (Ends("ance")) break;
                if (Ends("ence")) break;
                return;
            case 'e':
                if (Ends("er")) break;
                return;
            case 'i':
                if (Ends("ic")) break;
                return;
            case 'l':
                if (Ends("able")) break;
                if (Ends("ible")) break;
                return;
            case 'n':
                if (Ends("ant")) break;
                if (Ends("ement")) break;
                if (Ends("ment")) break;
                if (Ends("ent")) break;
                return;
            case 'o':
                if (Ends("ion") && _j >= 0 && _b[_j] is 's' or 't') break;
                if (Ends("ou")) break;
                return;
            case 's':
                if (Ends("ism")) break;
                return;
            case 't':
                if (Ends("ate")) break;
                if (Ends("iti")) break;
                return;
            case 'u':
                if (Ends("ous")) break;
                return;
            case 'v':
                if (Ends("ive")) break;
                return;
            case 'z':
                if (Ends("ize")) break;
                return;
            default:
                return;
        }
        if (Measure() > 1)
            _k = _j;
    }

    // Step 5: remove a final -e when m > 1 (or m = 1 and not *o), and -ll to -l when m > 1.
    private void Step5()
    {
        _j = _k;
        if (_b[_k] == 'e')
        {
            var m = Measure();
            if (m > 1 || (m == 1 && !Cvc(_k - 1)))
                _k--;
        }
        if (_b[_k] == 'l' && DoubleConsonant(_k) && Measure() > 1)
            _k--;
    }
}
