using Xunit;

namespace BeeBAK.EntityFrameworkCore;

[CollectionDefinition(BeeBAKTestConsts.CollectionDefinitionName)]
public class BeeBAKEntityFrameworkCoreCollection : ICollectionFixture<BeeBAKEntityFrameworkCoreFixture>
{

}
