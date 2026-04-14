using System.ComponentModel.DataAnnotations;

namespace SmartTrafficTool.Models;

public class ANPRTRAN_VEH_CLASSIFICATION
{
    [Key]
    [Required]
    public long VEH_CLASSIFICATION_ID { get; set; }

    public long TRANSACTION_ID { get; set; }

    public int? MNF_ID { get; set; }

    public int? MOD_ID { get; set; }

    public int? YEAR { get; set; }

    public int? CLR_ID { get; set; }

    public int? VEH_TYPE_ID { get; set; }
}
