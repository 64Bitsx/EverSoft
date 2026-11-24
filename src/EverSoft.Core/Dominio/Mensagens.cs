using System.Globalization;
using System.Text;
using EverSoft.Core.Configuracao;
using EverSoft.Core.Dados;
using EverSoft.Core.Util;

namespace EverSoft.Core.Dominio;

/// <summary>Textos enviados pelo WhatsApp (boletim e lista de faltosos).</summary>
public static class Mensagens
{
    /// <summary>0,856 vira "85.6" (igual ao CStr do VBA).</summary>
    public static string PctTexto(double frac) =>
        Math.Round(frac * 100, 10).ToString("G12", CultureInfo.InvariantCulture);

    /// <summary>Comentário conforme o nível de aproveitamento.</summary>
    public static string MensagemNivel(double frac, string proximoMes)
    {
        if (frac <= 0.79)
            return $"Foi um Nível de Aprendizado legal! Vamos nos esforçar nesse mês de `{proximoMes}` para aumentar esse aprendizado!";
        if (frac <= 0.89)
            return "Foi um Nível de Aprendizado `Excelente!` Mas sempre podemos melhorar, vamos nos esforçar " +
                   $"nesse mês de `{proximoMes}` para conseguir um aprendizado ainda melhor!";
        if (frac <= 0.99)
            return "Foi um Nível de Aprendizado `incrível!` Seu Aprendizado está muito alto! Parabéns! " +
                   "Está quase beirando a perfeição! Esforce-se para continuar assim!";
        if (frac == 1)
            return "Foi um Nível de Aprendizado `Perfeito!` Seu Aprendizado está muito alto! Parabéns! " +
                   "Esforce-se para continuar assim!";
        return "";
    }

    public static string MensagemBoletim(string nome, string mes, string proximoMes, double frac) =>
        $"Olá, `{nome}!` Ou responsável? Tudo bem? Fique de olho no seu Nível de Aprendizado de `{mes}!`\n\n" +
        $"Você obteve no mês de `{mes}` o Aprendizado de `{PctTexto(frac)}%!`\n\n{MensagemNivel(frac, proximoMes)}";

    /// <summary>Mensagem "FALTOSOS" para o grupo, com quem respondeu e quem não respondeu.</summary>
    public static (string Texto, int ComRetorno, int SemRetorno) MontarFaltosos(
        IEnumerable<RegistroJustifica> linhas, DataHub hub, string dia, string faixa, string codigo)
    {
        var com = new StringBuilder();
        var sem = new StringBuilder();
        int nCom = 0, nSem = 0;
        foreach (var l in linhas)
        {
            if (Texto.NormalizarHorario(l.Horario) != codigo) continue;
            string nome = (l.Nome ?? "").Trim();
            if (nome == "") continue;
            string just = (l.Justificativa ?? "").Trim();
            string mat = hub.MatriculaPorNome(nome);
            if (just != "")
            {
                com.Append($"- {mat} - {nome}: *{just}*\n");
                nCom++;
            }
            else
            {
                sem.Append($"- {mat} - {nome}: *MSG ENVIADA/SEM RETORNO*\n");
                nSem++;
            }
        }
        if (com.Length == 0 && sem.Length == 0)
            return ($"FALTOSOS {dia} {faixa}\n*Todos os alunos presentes!", 0, 0);
        var m = new StringBuilder($"FALTOSOS {dia} {faixa}\n\n");
        if (com.Length > 0) m.Append("✅ COM RETORNO:\n").Append(com);
        if (sem.Length > 0) m.Append("\n \n❌ SEM RETORNO:\n").Append(sem);
        string texto = m.ToString();
        if (texto.EndsWith('\n')) texto = texto[..^1];
        return (texto.ToUpper(new CultureInfo("pt-BR")), nCom, nSem);
    }
}
