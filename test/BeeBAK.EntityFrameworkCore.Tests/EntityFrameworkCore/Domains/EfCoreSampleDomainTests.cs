using BeeBAK.Samples;
using Xunit;

namespace BeeBAK.EntityFrameworkCore.Domains;

[Collection(BeeBAKTestConsts.CollectionDefinitionName)]
public class EfCoreSampleDomainTests : SampleDomainTests<BeeBAKEntityFrameworkCoreTestModule>
{

}
