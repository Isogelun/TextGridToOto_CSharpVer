using System;
using System.IO;
using System.Linq;
using System.Text;

namespace TextGrid2Oto;

public static class TextGridToOtoRunner
{
    public static void ConvertFromConfig(string configPath)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        if (string.IsNullOrWhiteSpace(configPath))
        {
            throw new ArgumentException("configPath 不能为空", nameof(configPath));
        }

        var config = IniLikeConfig.Load(configPath);

        var wavPath = config.Require("wav_path");
        var dsDictPath = config.Require("ds_dict");
        var presampPath = config.Require("presamp");

        var textGridPath = config.Get("TextGrid_path") ?? Path.Combine(wavPath, "TextGrid");
        var ignore = config.Get("ignore") ?? "AP,SP,EP,R,-,B";

        var modeRaw = config.Get("VCV_mode") ?? "0";
        var mode = modeRaw switch
        {
            "0" => OtoMode.Cvvc,
            "1" => OtoMode.Vcv,
            "2" => OtoMode.Cvv,
            _ => OtoMode.Cvvc
        };

        var cvSum = config.GetCsvDoubles("cv_sum") ?? [1, 3, 1.5, 1, 2];
        var vcSum = config.GetCsvDoubles("vc_sum") ?? [3, 0, 2, 1, 2];
        var vvSum = config.GetCsvDoubles("vv_sum") ?? [3, 3, 1.5, 1, 1.5];

        var cvOffset = config.GetCsvDoubles("cv_offset") ?? [0, 0, 0, 0, 0];
        var vcOffset = config.GetCsvDoubles("vc_offset") ?? [0, 0, 0, 0, 0];

        var cvRepeat = config.GetInt("CV_repeat") ?? 1;
        var vcRepeat = config.GetInt("VC_repeat") ?? 1;
        var pitch = config.Get("pitch") ?? "";
        var cover = config.Get("cover") ?? "N";

        var converter = new TextGrid2OtoConverter(
            dsDictPath: dsDictPath,
            presampPath: presampPath,
            ignoreCsv: ignore,
            mode: mode,
            cvSum: cvSum,
            vcSum: vcSum,
            vvSum: vvSum
        );

        var result = converter.ConvertTextGridDirectory(textGridPath);

        var cvRawPath = Path.Combine(wavPath, "cv_oto.ini");
        var vcRawPath = Path.Combine(wavPath, "vc_oto.ini");
        var finalPath = Path.Combine(wavPath, "oto.ini");

        OtoWriter.WriteRawOtoLines(cvRawPath, result.CvRawLines, cover: "Y");
        OtoWriter.WriteRawOtoLines(vcRawPath, result.VcRawLines, cover: "Y");

        var cvEntries = OtoPostProcessor.ParseRawLines(result.CvRawLines);
        var vcEntries = OtoPostProcessor.ParseRawLines(result.VcRawLines);

        cvEntries = OtoPostProcessor.ApplyRepeat(cvEntries, cvRepeat);
        vcEntries = OtoPostProcessor.ApplyRepeat(vcEntries, vcRepeat);

        if (!OtoPostProcessor.IsZeroOffset(cvOffset))
        {
            cvEntries = OtoPostProcessor.ApplyOffset(cvEntries, cvOffset);
        }

        if (!OtoPostProcessor.IsZeroOffset(vcOffset))
        {
            vcEntries = OtoPostProcessor.ApplyOffset(vcEntries, vcOffset);
        }

        OtoWriter.WriteFinalOto(finalPath, cvEntries.Concat(vcEntries), pitch, cover);
    }
}
