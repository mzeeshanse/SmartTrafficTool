using System.ComponentModel.DataAnnotations;

namespace SmartTrafficTool.Models;

public class ANPRTRAN
{
    [Key]
    [Required]
    public long TRANSACTION_ID { get; set; }

    [Required]
    public int DEVICE_ID { get; set; }

    public int LOCATION_ID { get; set; }

    public int ZONE_ID { get; set; }

    [Required]
    public DateTime CREATED_DATE_TIME { get; set; }

    [Required]
    public DateTime CREATED_DATE { get; set; }

    public int PLATE_NUMBER { get; set; }

    [MaxLength(4)]
    public string? PLATE_ALPHA_EN { get; set; }

    [MaxLength(4)]
    public string? PLATE_ALPHA { get; set; }

    [MaxLength(16)]
    public string? PLATE_TEXT { get; set; }

    [Required]
    public int PLATE_TYPE_ID { get; set; }

    [Required]
    public int PLATE_ORIGIN_ID { get; set; }

    [Required]
    public int PLATE_CONFIDENCE { get; set; }

    [Required]
    public int PLATE_COLOR_ID { get; set; }

    [MaxLength(20)]
    public string? PLATE_XY { get; set; }

    [Required]
    public int TRANSACTION_STATUS { get; set; }

    [Required]
    public int PLATE_STATUS { get; set; }

    [Required]
    public bool IS_MODIFIED { get; set; }

    [Required]
    public bool IS_ALERTED { get; set; }

    public int TRAN_TYPE_ID { get; set; }

    public int SPEED { get; set; }

    public bool DIRECTION_IS_FRONT { get; set; }

    public DateTime SOURCE_DATE_TIME { get; set; }

    public long? MEMBER_ID { get; set; }

    public long? VEHICLE_ID { get; set; }
}
