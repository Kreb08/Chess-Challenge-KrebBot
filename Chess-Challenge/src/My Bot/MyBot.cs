//#define POSITION
//#define LOG
using ChessChallenge.API;
using System;
using System.Linq;

// TODO: RFP, futility pruning, and LMR: https://discord.com/channels/1132289356011405342/1140697049046724678/1140699400889454703
public class MyBot : IChessBot
{
    private int[] piecePhase = { 0, 0, 1, 1, 2, 4, 0 };

    //										  p    k    b    r    q   k
    private readonly short[] PieceValues = { 82, 337, 365, 447, 1025, 0, // Midgame
                                             94, 281, 297, 512,  936, 0}; // Endgame

    // Big table packed with data from premade piece square tables
    // Unpack using PackedEvaluationTables[set, rank] = file
    private readonly decimal[] PackedPestoTables = {
            63746705523041458768562654720m, 71818693703096985528394040064m, 75532537544690978830456252672m, 75536154932036771593352371712m, 76774085526445040292133284352m, 3110608541636285947269332480m, 936945638387574698250991104m, 75531285965747665584902616832m,
            77047302762000299964198997571m, 3730792265775293618620982364m, 3121489077029470166123295018m, 3747712412930601838683035969m, 3763381335243474116535455791m, 8067176012614548496052660822m, 4977175895537975520060507415m, 2475894077091727551177487608m,
            2458978764687427073924784380m, 3718684080556872886692423941m, 4959037324412353051075877138m, 3135972447545098299460234261m, 4371494653131335197311645996m, 9624249097030609585804826662m, 9301461106541282841985626641m, 2793818196182115168911564530m,
            77683174186957799541255830262m, 4660418590176711545920359433m, 4971145620211324499469864196m, 5608211711321183125202150414m, 5617883191736004891949734160m, 7150801075091790966455611144m, 5619082524459738931006868492m, 649197923531967450704711664m,
            75809334407291469990832437230m, 78322691297526401047122740223m, 4348529951871323093202439165m, 4990460191572192980035045640m, 5597312470813537077508379404m, 4980755617409140165251173636m, 1890741055734852330174483975m, 76772801025035254361275759599m,
            75502243563200070682362835182m, 78896921543467230670583692029m, 2489164206166677455700101373m, 4338830174078735659125311481m, 4960199192571758553533648130m, 3420013420025511569771334658m, 1557077491473974933188251927m, 77376040767919248347203368440m,
            73949978050619586491881614568m, 77043619187199676893167803647m, 1212557245150259869494540530m, 3081561358716686153294085872m, 3392217589357453836837847030m, 1219782446916489227407330320m, 78580145051212187267589731866m, 75798434925965430405537592305m,
            68369566912511282590874449920m, 72396532057599326246617936384m, 75186737388538008131054524416m, 77027917484951889231108827392m, 73655004947793353634062267392m, 76417372019396591550492896512m, 74568981255592060493492515584m, 70529879645288096380279255040m,
        };
    // pesto[64][12] 64 squares with values flipped for white, 12 piecetypes (first 6 mg, last 6 eg) pawn_mg, knight_mg, ..., queen_eg, king_eg
    private readonly int[][] unpackedPestoTables;

    private const ulong k_TpMask = 0x7FFFFF; //4.7 million entries, likely consuming about 151 MB, 01111111_11111111_11111111
    private readonly Transposition[] transposTable; // ref  m_TPTable[zHash & k_TpMask]

	// ulong 1 is WHITE BOTTOM LEFT (A1), MAX VALUE is EVERY TILE (11111....)

    int max_depth = 50; // n + 1, max depth will be 1 smaller
    Timer timer;

#if LOG
    int nullWindow = 0; //#DEBUG
#endif

    bool outOfTime() => timer.MillisecondsElapsedThisTurn > timer.MillisecondsRemaining / 30;

	public MyBot()
	{
		transposTable = new Transposition[k_TpMask + 1];

        unpackedPestoTables = PackedPestoTables.Select(packedTable =>
        {
            int pieceType = 0;
            return decimal.GetBits(packedTable).Take(3)
                .SelectMany(c => BitConverter.GetBytes(c)
                    .Select((byte square) => (int)((sbyte)square * 1.461) + PieceValues[pieceType++]))
                .ToArray();
        }).ToArray();
    }

	public Move Think(Board board, Timer timer)
    {
#if LOG
        nullWindow = 0; //#DEBUG
#endif
        Transposition bestMove = transposTable[board.ZobristKey & k_TpMask];
        this.timer = timer;

#if POSITION
        string fen = "1n1rr3/ppp1qpkp/3p2p1/3P4/5p2/PP2P1PB/3P3P/R2Q1RK1 w - - 0 17";
        return printScores(fen, 7);
#endif

        if (timer.MillisecondsRemaining < 1000)
			max_depth = 4;

        int beta = int.MaxValue, 
            alpha = -beta, 
            depth = 1;
        while (depth < max_depth)
		{
			int score = CalcRecursive(board, depth, board.IsWhiteToMove ? 1 : -1, alpha, beta);
			bestMove = transposTable[board.ZobristKey & k_TpMask];

            // time management
            if (outOfTime())
                break;

            // Aspiration Search
            if (score < alpha || score > beta)
            {
#if LOG
                Console.WriteLine("Research depth: " + depth); //#DEBUG
#endif
                alpha = int.MinValue + 1;
                beta = int.MaxValue;
            }
            else
            {
                depth++;
                alpha = score - 30;
                beta = score + 30;
            }
        }
#if LOG
        Console.WriteLine("Search depth: " + depth + ", Score: " + bestMove.evaluation + ", NullWindows: " + nullWindow); //#DEBUG
#endif
        return bestMove.zobristHash == board.ZobristKey ? bestMove.move : board.GetLegalMoves()[0];
	}

    int CalcRecursive(Board board, int currentDepth, int side, int alpha, int beta)
	{
        if (board.IsRepeatedPosition())
            return 0;

        ref Transposition transposition = ref transposTable[board.ZobristKey & k_TpMask];

        // TT cutoffs
        if (transposition.zobristHash == board.ZobristKey && transposition.depth >= currentDepth && (
			transposition.bound == 3 // exact score
            || transposition.bound == 2 && transposition.evaluation >= beta // lower bound, fail high
            || transposition.bound == 1 && transposition.evaluation <= alpha // upper bound, fail low
        )) return transposition.evaluation;

        bool qsearch = currentDepth <= 0;
        int eval = EvalMaterial(board, side);
        int bestScore = int.MinValue;

        // Quiescence search is in the same function as negamax to save tokens
        if (qsearch)
        {
            bestScore = eval;
            if (bestScore >= beta) 
                return bestScore;
            alpha = Math.Max(alpha, bestScore);
        }

        Span<Move> moves = stackalloc Move[256];
		board.GetLegalMovesNonAlloc(ref moves, qsearch);
        int[] scores = new int[moves.Length];

        // Score moves
        for (int i = 0; i < moves.Length; i++)
        {
            Move move = moves[i];
            // TT move
            if (move == transposition.move) 
				scores[i] = 1000000;
            // https://www.chessprogramming.org/MVV-LVA
            else if (move.IsCapture)
				scores[i] = 100 * (int)move.CapturePieceType - (int)move.MovePieceType;
        }


		Move bestMove = Move.NullMove;
        int origAlpha = alpha;
        for (int i = 0; i < moves.Length; i++)
        {
#if !POSITION
            if (outOfTime())
                return 1_000_000;
#endif

            // Incrementally sort moves
            for (int j = i + 1; j < moves.Length; j++)
            {
                if (scores[j] > scores[i])
                    (scores[i], scores[j], moves[i], moves[j]) = (scores[j], scores[i], moves[j], moves[i]);
            }

			Move move = moves[i];
            board.MakeMove(move);
            int score;
            //PVS
            if (i == 0) // first move
                score = -CalcRecursive(board, currentDepth - 1, -side, -beta, -alpha);
            else
            {
                score = -CalcRecursive(board, currentDepth - 1, -side, -alpha-1, -alpha);
                if (score > alpha && score < beta)
                {
#if LOG
                    nullWindow++; //#DEBUG
#endif
                    score = -CalcRecursive(board, currentDepth - 1, -side, -beta, -alpha);
                }
            }
            board.UndoMove(move);
			if (score > bestScore)
            {
				bestScore = score;
                bestMove = move;
                alpha = Math.Max(alpha, score);
                // Fail soft beta
                if (score >= beta)
                    break;
            }
        }

        // End cases
        if (!qsearch && moves.Length == 0)
            return board.IsInCheck() ? board.PlyCount - 300_000 : 0;

        // Did we fail high/low or get an exact score?
        int bound = bestScore >= beta ? 3 : bestScore > origAlpha ? 2 : 1;

        // update cache with best move
        transposition.zobristHash = board.ZobristKey;
		transposition.move = bestMove;
		transposition.evaluation = bestScore;
		transposition.depth = (sbyte) currentDepth;
		transposition.bound = (sbyte) bound;
		return bestScore;
	}

	// pieces of current player - pieces oppenent player + move options current player
	int EvalMaterial(Board board, int side)
	{
		int mg = 0, // midgame Values
            eg = 0, // endgame Values
            phase = 0; // progress to endgame

        // sum up values, according to white side
        foreach (bool stm in new[] { true, false })
        {
            for (PieceType p = PieceType.Pawn; p <= PieceType.King; p++)
            {
                int pieceType = (int) p;
                ulong pieces = board.GetPieceBitboard(p, stm);
                while (pieces > 0)
                {
                    phase += piecePhase[pieceType];
                    int pieceIndex = BitboardHelper.ClearAndGetIndexOfLSB(ref pieces) ^ (stm ? 56 : 0); // flip for white (tables are for black side)
                    mg += unpackedPestoTables[pieceIndex][pieceType - 1];
                    eg += unpackedPestoTables[pieceIndex][pieceType + 5];
                }
            }
            // sum up white values, negate, sum up black values, negate again
            // essentialy: white - black
            mg = -mg;
            eg = -eg;
        }
        phase = Math.Min(phase, 24);
        // combine scores and flip score depending on side
        return (mg * phase + eg * (24 - phase)) / 24 * side;
	}

    //14 bytes per entry, likely will align to 16 bytes due to padding (if it aligns to 32, recalculate max TP table size)
	public struct Transposition
	{
		public ulong zobristHash;
		public Move move;
		public int evaluation;
		public sbyte depth;
		public sbyte bound; // 2 = Lower Bound, 1 = Upper Bound, 3 = Exact
	};

#if POSITION
    public Move printScores(string fenString, int depth)
    {
        Board board = Board.CreateBoardFromFEN(fenString);
        Console.WriteLine(board.CreateDiagram());
        Move[] moves = board.GetLegalMoves();
        int[][] scores = new int[moves.Length][];

        for (int i = 0; i < moves.Length; i++)
        {
            scores[i] = new int[depth];
        }

        for (int d = 0; d < depth; d++)
        {
            for (int i = 0; i < moves.Length; i++)
            {
                board.MakeMove(moves[i]);
                scores[i][d] = -CalcRecursive(board, d, board.IsWhiteToMove ? 1 : -1, -1_000_000, 1_000_000);
                board.UndoMove(moves[i]);
            }
        }
        var orderedMoves = moves.Zip(scores).OrderByDescending(tuple => tuple.Second.Last()).Take(10).Select(tuple => tuple.First + " | " + tuple.Second.Select(i => string.Format("{0,11}", i)).Aggregate((first, second) => first + " | " + second)).ToArray();
        Console.WriteLine("Depth: " + depth);
        foreach (string s in orderedMoves)
        {
            Console.WriteLine(s);
        }
        Console.WriteLine(timer);
        return moves[0];
    }
#endif
}