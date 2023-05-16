using System.ComponentModel.DataAnnotations.Schema;

namespace Bow2.FA.Models
{
    public partial class Match
    {
        [NotMapped]
        public bool IsModified { get; set; } = false;
    }
}
